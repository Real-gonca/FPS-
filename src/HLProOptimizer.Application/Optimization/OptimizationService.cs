using System.Diagnostics;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Core.Plugins;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization;

/// <summary>
/// Orquestrador da otimização: monta o plano de execução a partir do modo, do
/// nível e dos toggles selecionados, executa os passos sequencialmente com
/// progresso/log em tempo real e consolida o resultado final.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que sequencial?</b> Passos de otimização mexem no mesmo recurso
/// escasso (disco e registro) e têm dependências implícitas: o ponto de
/// restauração precisa existir antes de qualquer alteração destrutiva, e o
/// reinício do Explorer deve ser o último. Paralelizar traria risco sem ganho
/// perceptível - o gargalo é I/O, não CPU.
/// </para>
/// <para>
/// <b>Falha isolada:</b> cada passo é encapsulado em try/catch pela
/// <see cref="OptimizationStepBase"/>; um erro nunca aborta a otimização.
/// </para>
/// </remarks>
public sealed class OptimizationService : IOptimizationService
{
    private readonly IReadOnlyList<IOptimizationStep> _steps;
    private readonly ISystemInformationService _systemInformation;
    private readonly ISettingsService _settingsService;
    private readonly IPluginManager _pluginManager;
    private readonly IActionHistoryService _history;
    private readonly ILogger<OptimizationService> _logger;

    /// <summary>Cria o orquestrador.</summary>
    /// <param name="steps">Todos os passos registrados no DI.</param>
    /// <param name="systemInformation">Informações do sistema (perfil + estado de elevação).</param>
    /// <param name="settingsService">Configurações do usuário.</param>
    /// <param name="pluginManager">Plugins ativos (passos adicionais).</param>
    /// <param name="history">Registro do histórico de ações.</param>
    /// <param name="logger">Logger.</param>
    public OptimizationService(
        IEnumerable<IOptimizationStep> steps,
        ISystemInformationService systemInformation,
        ISettingsService settingsService,
        IPluginManager pluginManager,
        IActionHistoryService history,
        ILogger<OptimizationService> logger)
    {
        _steps = steps.OrderBy(s => s.Order).ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToList();
        _systemInformation = systemInformation;
        _settingsService = settingsService;
        _pluginManager = pluginManager;
        _history = history;
        _logger = logger;

        logger.LogInformation("OptimizationService inicializado com {Count} passo(s).", _steps.Count);
    }

    /// <inheritdoc />
    public IReadOnlyList<IOptimizationStep> AvailableSteps => _steps;

    /// <inheritdoc />
    public IReadOnlyList<IOptimizationStep> GetPlan(OptimizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return _steps
            .Where(step => IsPlanned(step, options))
            .OrderBy(step => step.Order)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<OptimizationResult> OptimizeAsync(
        OptimizationOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var started = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Iniciando otimização (modo={Mode}, nível={Level}).",
            options.Mode, options.Level);

        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var profile = await _systemInformation.GetSystemProfileAsync(cancellationToken).ConfigureAwait(false);
        var isElevated = _systemInformation.IsAdministrator;

        var context = new OptimizationContext(options, settings, profile);
        var plan = GetPlan(options);
        var results = new List<OptimizationStepResult>();
        var requiresElevation = false;

        if (plan.Count == 0)
        {
            _logger.LogWarning("Plano de otimização vazio para modo={Mode}/nível={Level}.", options.Mode, options.Level);
        }

        for (var index = 0; index < plan.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var step = plan[index];

            progress?.Report(new ScanProgress(
                MapCategory(step),
                step.Name,
                index,
                plan.Count,
                step.Description));

            // Passos administrativos são ignorados sem elevação (e sinalizados à UI).
            if (step.RequiresAdmin && !isElevated)
            {
                requiresElevation = true;
                context.Log($"⏭ {step.Name}: ignorado - requer administrador.");

                results.Add(new OptimizationStepResult(
                    step.Id, step.Name, false, "Ignorado: requer privilégios de administrador.", 0, true, TimeSpan.Zero));

                continue;
            }

            var result = await step.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        // Passos extras dos plugins ativos (Open/Closed: não precisam conhecer o orquestrador).
        results.AddRange(await ExecutePluginStepsAsync(options, context, cancellationToken).ConfigureAwait(false));

        stopwatch.Stop();

        var optimizationResult = new OptimizationResult
        {
            Mode = options.Mode,
            Level = options.Level,
            StartedAt = started,
            CompletedAt = DateTime.Now,
            Steps = results,
            RequiresElevation = requiresElevation
        };

        _logger.LogInformation(
            "Otimização finalizada em {Elapsed}: {Success} sucesso, {Skipped} ignorado(s), {Failed} falha(s), {Bytes} liberados.",
            stopwatch.Elapsed,
            optimizationResult.SuccessCount,
            optimizationResult.SkippedCount,
            optimizationResult.FailedCount,
            optimizationResult.TotalBytesFreed);

        await _history.RecordOptimizationAsync(optimizationResult, cancellationToken).ConfigureAwait(false);

        return optimizationResult;
    }

    /// <inheritdoc />
    public async Task<OptimizationResult> RevertAsync(
        OptimizationMode mode,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var started = DateTime.Now;
        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var profile = await _systemInformation.GetSystemProfileAsync(cancellationToken).ConfigureAwait(false);

        var options = new OptimizationOptions { Mode = mode, Level = OptimizationLevel.Aggressive };
        var context = new OptimizationContext(options, settings, profile);

        // Reverte na ordem inversa da aplicação.
        var reversible = _steps
            .Where(step => step.SupportedModes.Contains(mode) && step.IsReversible)
            .OrderByDescending(step => step.Order)
            .ToList();

        var results = new List<OptimizationStepResult>();
        var requiresElevation = false;

        for (var index = 0; index < reversible.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var step = reversible[index];

            progress?.Report(new ScanProgress(
                MapCategory(step),
                step.Name,
                index,
                reversible.Count,
                $"Revertendo {step.Name}..."));

            if (step.RequiresAdmin && !_systemInformation.IsAdministrator)
            {
                requiresElevation = true;
                results.Add(new OptimizationStepResult(step.Id, step.Name, false, "Ignorado: requer administrador.", 0, true, TimeSpan.Zero));
                continue;
            }

            try
            {
                var result = await step.RevertAsync(context, cancellationToken).ConfigureAwait(false);
                results.Add(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha ao reverter o passo {StepId}.", step.Id);
                results.Add(new OptimizationStepResult(step.Id, step.Name, false, $"Falha ao reverter: {ex.Message}", 0, false, TimeSpan.Zero, ex.ToString()));
            }
        }

        var optimizationResult = new OptimizationResult
        {
            Mode = mode,
            Level = OptimizationLevel.Aggressive,
            StartedAt = started,
            CompletedAt = DateTime.Now,
            Steps = results,
            RequiresElevation = requiresElevation
        };

        await _history.RecordAsync(
            ActionKind.Optimization,
            $"Reversão do modo {mode}",
            optimizationResult.Summary,
            optimizationResult.TotalBytesFreed,
            (long)optimizationResult.Duration.TotalMilliseconds,
            optimizationResult.IsFullySuccessful,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return optimizationResult;
    }

    /// <summary>Define se um passo entra no plano para as opções informadas.</summary>
    private static bool IsPlanned(IOptimizationStep step, OptimizationOptions options)
    {
        if (!step.SupportedModes.Contains(options.Mode))
        {
            return false;
        }

        if (options.Level < step.MinimumLevel)
        {
            return false;
        }

        // Passos explicitamente desligados pelo usuário nunca entram no plano.
        return IsToggleEnabled(step.Id, options);
    }

    /// <summary>Mapa entre o identificador do passo e o toggle correspondente na UI.</summary>
    private static bool IsToggleEnabled(string stepId, OptimizationOptions options) => stepId switch
    {
        OptimizationStepIds.TemporaryFiles => options.CleanTemporaryFiles,
        OptimizationStepIds.SystemCache => options.CleanSystemCache,
        OptimizationStepIds.BrowserCache => options.CleanBrowserCache,
        OptimizationStepIds.RecycleBin => options.EmptyRecycleBin,
        OptimizationStepIds.Registry => options.OptimizeRegistry,
        OptimizationStepIds.DnsFlush => options.FlushDns,
        OptimizationStepIds.NetworkLatency => options.OptimizeNetworkLatency,
        OptimizationStepIds.Services => options.OptimizeServices,
        OptimizationStepIds.Telemetry => options.DisableTelemetry,
        OptimizationStepIds.Startup => options.OptimizeStartup,
        OptimizationStepIds.UltimatePerformance => options.EnableMaximumPerformance,
        OptimizationStepIds.RestorePoint => options.CreateRestorePoint,
        OptimizationStepIds.RestartExplorer => options.RestartExplorer,
        _ => true
    };

    /// <summary>Executa as otimizações dos plugins ativos.</summary>
    private async Task<IReadOnlyList<OptimizationStepResult>> ExecutePluginStepsAsync(
        OptimizationOptions options,
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        var plugins = _pluginManager.LoadedPlugins;

        if (plugins.Count == 0)
        {
            return [];
        }

        var results = new List<OptimizationStepResult>();

        foreach (var plugin in plugins)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (plugin.RequiresAdmin && !_systemInformation.IsAdministrator)
            {
                results.Add(new OptimizationStepResult(
                    $"plugin.{plugin.Id}", plugin.Name, false, "Plugin ignorado: requer administrador.", 0, true, TimeSpan.Zero));
                continue;
            }

            try
            {
                context.Log($"→ Plugin {plugin.Name} v{plugin.Version}");
                var result = await plugin.OptimizeAsync(options, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                context.AddFreedBytes(result.BytesFreed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Plugin '{Plugin}' falhou durante a otimização.", plugin.Id);
                results.Add(new OptimizationStepResult(
                    $"plugin.{plugin.Id}", plugin.Name, false, $"Falha no plugin: {ex.Message}", 0, false, TimeSpan.Zero, ex.ToString()));
            }
        }

        return results;
    }

    /// <summary>Associa cada passo a uma categoria, para a barra de progresso da UI.</summary>
    private static IssueCategory MapCategory(IOptimizationStep step) => step.Id switch
    {
        OptimizationStepIds.TemporaryFiles => IssueCategory.TemporaryFiles,
        OptimizationStepIds.SystemCache => IssueCategory.SystemCache,
        OptimizationStepIds.BrowserCache => IssueCategory.BrowserCache,
        OptimizationStepIds.RecycleBin => IssueCategory.RecycleBin,
        OptimizationStepIds.Registry => IssueCategory.Registry,
        OptimizationStepIds.Services => IssueCategory.Services,
        OptimizationStepIds.Telemetry => IssueCategory.Privacy,
        OptimizationStepIds.Startup => IssueCategory.StartupPrograms,
        OptimizationStepIds.DnsFlush or OptimizationStepIds.NetworkLatency => IssueCategory.Network,
        OptimizationStepIds.RestorePoint => IssueCategory.Security,
        _ => IssueCategory.Performance
    };
}

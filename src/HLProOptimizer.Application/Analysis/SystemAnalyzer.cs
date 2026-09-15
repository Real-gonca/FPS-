using System.Diagnostics;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Analysis;

/// <summary>
/// Orquestrador do Scan Completo.
/// </summary>
/// <remarks>
/// Estratégia de execução:
/// <list type="number">
///   <item><description><b>Pré-coleta</b>: perfil do sistema, alvos de limpeza, programas de
///   inicialização, serviços, itens de privacidade e drivers são coletados UMA vez
///   (em paralelo) e colocados no <see cref="AnalysisContext"/>.</description></item>
///   <item><description><b>Regras em paralelo</b>: cada <see cref="IAnalysisRule"/> roda
///   concorrentemente via TPL a partir do contexto compartilhado.</description></item>
///   <item><description><b>Consolidação</b>: os problemas são agrupados no <see cref="ScanReport"/>,
///   ordenados por severidade e persistidos para o relatório completo.</description></item>
/// </list>
/// Falhas individuais de regras são isoladas e registradas em
/// <see cref="ScanReport.FailedCategories"/> - um scan nunca falha por completo.
/// </remarks>
public sealed class SystemAnalyzer : ISystemAnalyzer
{
    private readonly IReadOnlyList<IAnalysisRule> _rules;
    private readonly ISystemInformationService _systemInformation;
    private readonly ISettingsService _settingsService;
    private readonly ICleanupService _cleanupService;
    private readonly IStartupManager _startupManager;
    private readonly IServiceManager _serviceManager;
    private readonly IPrivacyService _privacyService;
    private readonly IOptimizationService _optimizationService;
    private readonly IScanReportRepository _reportRepository;
    private readonly IActionHistoryService _history;
    private readonly ILogger<SystemAnalyzer> _logger;

    /// <summary>Cria o analisador do sistema.</summary>
    public SystemAnalyzer(
        IEnumerable<IAnalysisRule> rules,
        ISystemInformationService systemInformation,
        ISettingsService settingsService,
        ICleanupService cleanupService,
        IStartupManager startupManager,
        IServiceManager serviceManager,
        IPrivacyService privacyService,
        IOptimizationService optimizationService,
        IScanReportRepository reportRepository,
        IActionHistoryService history,
        ILogger<SystemAnalyzer> logger)
    {
        _rules = rules.OrderBy(r => r.Order).ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
        _systemInformation = systemInformation;
        _settingsService = settingsService;
        _cleanupService = cleanupService;
        _startupManager = startupManager;
        _serviceManager = serviceManager;
        _privacyService = privacyService;
        _optimizationService = optimizationService;
        _reportRepository = reportRepository;
        _history = history;
        _logger = logger;

        logger.LogInformation("SystemAnalyzer inicializado com {Count} regra(s).", _rules.Count);
    }

    /// <inheritdoc />
    public IReadOnlyList<(IssueCategory Category, string Name)> Categories =>
        _rules
            .GroupBy(r => r.Category)
            .Select(g => (g.Key, g.First().Name))
            .ToList();

    /// <inheritdoc />
    public Task<ScanReport> AnalyzeAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
        => AnalyzeCoreAsync(_rules, progress, cancellationToken);

    /// <inheritdoc />
    public Task<ScanReport> AnalyzeAsync(
        IReadOnlyCollection<IssueCategory> categories,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categories);

        if (categories.Count == 0)
        {
            return AnalyzeCoreAsync(_rules, progress, cancellationToken);
        }

        var selected = _rules.Where(r => categories.Contains(r.Category)).ToList();

        return AnalyzeCoreAsync(selected, progress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OptimizationResult> FixAsync(
        IReadOnlyList<AnalysisIssue> issues,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var started = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();

        var fixIds = issues
            .Where(i => i.CanAutoFix && !string.IsNullOrWhiteSpace(i.FixIdentifier))
            .Select(i => i.FixIdentifier!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fixIds.Count == 0)
        {
            _logger.LogInformation("Nenhuma correção automática disponível para os problemas selecionados.");

            return new OptimizationResult
            {
                Mode = OptimizationMode.Quick,
                StartedAt = started,
                CompletedAt = DateTime.Now,
                Steps = []
            };
        }

        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var profile = await _systemInformation.GetSystemProfileAsync(cancellationToken).ConfigureAwait(false);
        var isElevated = _systemInformation.IsAdministrator;

        var options = new OptimizationOptions
        {
            Mode = OptimizationMode.Full,
            Level = settings.OptimizationLevel,
            CreateRestorePoint = settings.CreateRestorePointBeforeChanges
        };

        var context = new OptimizationContext(options, settings, profile);

        var steps = _optimizationService.AvailableSteps
            .Where(s => fixIds.Contains(s.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(s => s.Order)
            .ToList();

        var results = new List<OptimizationStepResult>();
        var requiresElevation = false;

        for (var index = 0; index < steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var step = steps[index];

            progress?.Report(new ScanProgress(
                IssueCategory.Performance,
                step.Name,
                index,
                steps.Count,
                $"Corrigindo: {step.Description}"));

            if (step.RequiresAdmin && !isElevated)
            {
                requiresElevation = true;
                results.Add(new OptimizationStepResult(step.Id, step.Name, false, "Ignorado: requer administrador.", 0, true, TimeSpan.Zero));
                continue;
            }

            results.Add(await step.ExecuteAsync(context, cancellationToken).ConfigureAwait(false));
        }

        stopwatch.Stop();

        var result = new OptimizationResult
        {
            Mode = OptimizationMode.Full,
            Level = options.Level,
            StartedAt = started,
            CompletedAt = DateTime.Now,
            Steps = results,
            RequiresElevation = requiresElevation
        };

        await _history.RecordAsync(
            ActionKind.Optimization,
            "Correção de problemas da análise",
            result.Summary,
            result.TotalBytesFreed,
            stopwatch.ElapsedMilliseconds,
            result.IsFullySuccessful,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>Executa o scan: pré-coleta paralela + regras em paralelo + consolidação.</summary>
    private async Task<ScanReport> AnalyzeCoreAsync(
        IReadOnlyList<IAnalysisRule> rules,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var started = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Iniciando análise do sistema com {Count} regra(s).", rules.Count);

        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var profile = await _systemInformation.GetSystemProfileAsync(cancellationToken).ConfigureAwait(false);
        var context = new AnalysisContext(profile, settings);

        progress?.Report(new ScanProgress(IssueCategory.Performance, "Coletando informações do sistema", 0, rules.Count, "Coletando informações do sistema..."));

        await PrefetchAsync(context, cancellationToken).ConfigureAwait(false);

        var issues = new List<AnalysisIssue>();
        var failedCategories = new List<string>();
        var completed = 0;
        var total = rules.Count;

        // Executa as regras em paralelo. O grau de paralelismo é limitado para não
        // saturar o disco durante a varredura de arquivos.
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
            CancellationToken = cancellationToken
        };

        try
        {
            await Parallel.ForEachAsync(rules, options, async (rule, token) =>
            {
                try
                {
                    var ruleIssues = await rule.AnalyzeAsync(context, token).ConfigureAwait(false);
                    var step = Interlocked.Increment(ref completed);

                    progress?.Report(new ScanProgress(
                        rule.Category,
                        rule.Name,
                        step,
                        total,
                        $"{ruleIssues.Count} problema(s) em '{rule.Name}'."));

                    lock (issues)
                    {
                        issues.AddRange(ruleIssues);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref completed);
                    _logger.LogError(ex, "Falha na regra de análise '{RuleId}' ({Category}).", rule.Id, rule.Category);

                    lock (failedCategories)
                    {
                        failedCategories.Add(rule.Category.ToString());
                    }
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Análise cancelada pelo usuário após {Completed}/{Total} regra(s).", completed, total);
            throw;
        }

        stopwatch.Stop();

        var report = new ScanReport
        {
            Id = Guid.NewGuid(),
            StartedAt = started,
            CompletedAt = DateTime.Now,
            Issues = issues
                .OrderByDescending(i => i.Severity)
                .ThenByDescending(i => i.RecoverableBytes)
                .ThenBy(i => i.Title, StringComparer.CurrentCulture)
                .ToList(),
            FailedCategories = failedCategories.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        _logger.LogInformation(
            "Análise concluída em {Elapsed}: {Issues} problema(s), {Bytes} recuperáveis, {Failed} categoria(s) com falha.",
            stopwatch.Elapsed,
            report.Issues.Count,
            report.TotalRecoverableBytes,
            report.FailedCategories.Count);

        progress?.Report(new ScanProgress(
            IssueCategory.Performance,
            "Concluído",
            total,
            total,
            $"Análise concluída: {report.Issues.Count} problema(s) encontrado(s)."));

        await PersistAsync(report, cancellationToken).ConfigureAwait(false);

        return report;
    }

    /// <summary>
    /// Coleta os dados compartilhados uma única vez, em paralelo, para que as
    /// regras não repitam chamadas WMI/registro/disco.
    /// </summary>
    private async Task PrefetchAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var settingsLevel = context.Settings.OptimizationLevel;

        var cleanupTask = SafeAsync("limpeza", () => _cleanupService.GetTargetsAsync(settingsLevel, cancellationToken), cancellationToken);
        var startupTask = SafeAsync("inicialização", () => _startupManager.GetStartupProgramsAsync(cancellationToken), cancellationToken);
        var servicesTask = SafeAsync("serviços", () => _serviceManager.GetServicesAsync(cancellationToken), cancellationToken);
        var privacyTask = SafeAsync("privacidade", () => _privacyService.GetItemsAsync(cancellationToken), cancellationToken);
        var driversTask = SafeAsync("drivers", () => _systemInformation.GetProblemDevicesAsync(cancellationToken), cancellationToken);

        await Task.WhenAll(cleanupTask, startupTask, servicesTask, privacyTask, driversTask).ConfigureAwait(false);

        context.CleanupTargets = await cleanupTask.ConfigureAwait(false);
        context.StartupPrograms = await startupTask.ConfigureAwait(false);
        context.Services = await servicesTask.ConfigureAwait(false);
        context.PrivacyItems = await privacyTask.ConfigureAwait(false);
        context.ProblemDrivers = await driversTask.ConfigureAwait(false);
    }

    /// <summary>Executa uma coleta com fallback seguro (falha de uma coleta não derruba o scan).</summary>
    private async Task<IReadOnlyList<T>> SafeAsync<T>(
        string name,
        Func<Task<IReadOnlyList<T>>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha na pré-coleta de dados de {Name}; seguindo com lista vazia.", name);
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }
    }

    /// <summary>Persiste o relatório e o registra no histórico (melhor esforço).</summary>
    private async Task PersistAsync(ScanReport report, CancellationToken cancellationToken)
    {
        try
        {
            await _reportRepository.SaveAsync(report, cancellationToken).ConfigureAwait(false);
            await _history.RecordAnalysisAsync(report, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Não foi possível persistir o relatório da análise.");
        }
    }
}

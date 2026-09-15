using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Rebaixa o tipo de inicialização de serviços não essenciais conforme o
/// <see cref="ServiceOptimizationCatalog"/>. Serviços críticos nunca são tocados.
/// </summary>
public sealed class ServiceOptimizationStep : OptimizationStepBase
{
    private readonly IServiceManager _serviceManager;

    /// <summary>Cria o passo.</summary>
    /// <param name="serviceManager">Gerenciador de serviços.</param>
    /// <param name="logger">Logger.</param>
    public ServiceOptimizationStep(IServiceManager serviceManager, ILogger<ServiceOptimizationStep> logger)
        : base(logger)
    {
        _serviceManager = serviceManager;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.Services;

    /// <inheritdoc />
    public override string Name => "Otimizar serviços";

    /// <inheritdoc />
    public override string Description => "Coloca em manual/desativado os serviços de telemetria e os não essenciais.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Full,
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override OptimizationLevel MinimumLevel => OptimizationLevel.Balanced;

    /// <inheritdoc />
    public override int Order => 80;

    /// <inheritdoc />
    public override bool RequiresAdmin => true;

    /// <inheritdoc />
    public override bool IsReversible => true;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.OptimizeServices)
        {
            return (true, "Desativado nas opções.", 0);
        }

        var tweaks = ServiceOptimizationCatalog.ForLevel(context.Options.Level);

        if (tweaks.Count == 0)
        {
            return (true, "Nenhum serviço elegível para o nível selecionado.", 0);
        }

        var changed = 0;
        var failed = 0;

        // Guarda o estado anterior para permitir reversão.
        var previous = new Dictionary<string, ServiceStartupKind>(StringComparer.OrdinalIgnoreCase);

        foreach (var tweak in tweaks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var service = await _serviceManager.GetServiceAsync(tweak.ServiceName, cancellationToken).ConfigureAwait(false);

                if (service is null)
                {
                    continue;
                }

                if (service.IsCritical || ServiceOptimizationCatalog.ProtectedServices.Contains(tweak.ServiceName))
                {
                    Logger.LogDebug("Serviço crítico '{Service}' ignorado pela política de proteção.", tweak.ServiceName);
                    continue;
                }

                if (service.StartupKind == tweak.TargetStartup)
                {
                    continue;
                }

                previous[tweak.ServiceName] = service.StartupKind;

                if (await _serviceManager.SetStartupKindAsync(tweak.ServiceName, tweak.TargetStartup, cancellationToken).ConfigureAwait(false))
                {
                    changed++;

                    // Serviços desabilitados que estão rodando são parados para efeito imediato.
                    if (tweak.TargetStartup == ServiceStartupKind.Disabled && service.IsRunning)
                    {
                        await _serviceManager.StopAsync(tweak.ServiceName, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                Logger.LogWarning(ex, "Falha ao otimizar o serviço '{Service}'.", tweak.ServiceName);
            }
        }

        context.State[$"{Id}.previous"] = previous;

        Logger.LogInformation("Serviços otimizados: {Changed} alterado(s), {Failed} falha(s).", changed, failed);

        return changed > 0
            ? (failed == 0, $"{changed} serviço(s) otimizado(s){(failed > 0 ? $", {failed} falha(s)" : string.Empty)}.", 0)
            : (failed == 0, "Todos os serviços já estavam otimizados.", 0);
    }

    /// <inheritdoc />
    public override async Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        if (context.State.TryGetValue($"{Id}.previous", out var state) &&
            state is Dictionary<string, ServiceStartupKind> previous)
        {
            foreach (var (serviceName, startupKind) in previous)
            {
                await _serviceManager.SetStartupKindAsync(serviceName, startupKind, cancellationToken).ConfigureAwait(false);
            }

            return new OptimizationStepResult(Id, Name, true, $"{previous.Count} serviço(s) restaurado(s).");
        }

        return new OptimizationStepResult(Id, Name, true, "Nenhum estado anterior registrado para reverter.", 0, true);
    }
}

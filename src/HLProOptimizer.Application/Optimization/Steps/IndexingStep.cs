using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Desativa a indexação de busca (WSearch) para eliminar I/O de disco concorrente
/// durante os jogos. Reversível: volta para Automático (Delayed) ao desativar.
/// </summary>
public sealed class IndexingStep : OptimizationStepBase
{
    private const string ServiceName = "WSearch";

    private readonly IServiceManager _serviceManager;

    /// <summary>Cria o passo.</summary>
    /// <param name="serviceManager">Gerenciador de serviços.</param>
    /// <param name="logger">Logger.</param>
    public IndexingStep(IServiceManager serviceManager, ILogger<IndexingStep> logger)
        : base(logger)
    {
        _serviceManager = serviceManager;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.Indexing;

    /// <inheritdoc />
    public override string Name => "Desativar indexação de busca";

    /// <inheritdoc />
    public override string Description => "Coloca o Windows Search em manual e o interrompe, liberando I/O de disco.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override OptimizationLevel MinimumLevel => OptimizationLevel.Aggressive;

    /// <inheritdoc />
    public override int Order => 130;

    /// <inheritdoc />
    public override bool RequiresAdmin => true;

    /// <inheritdoc />
    public override bool IsReversible => true;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        var service = await _serviceManager.GetServiceAsync(ServiceName, cancellationToken).ConfigureAwait(false);

        if (service is null)
        {
            return (true, "O serviço Windows Search não existe neste sistema.", 0);
        }

        context.State[$"{Id}.previous"] = service.StartupKind;

        if (service.StartupKind == ServiceStartupKind.Manual && !service.IsRunning)
        {
            return (true, "A indexação já estava desativada.", 0);
        }

        var changed = await _serviceManager.SetStartupKindAsync(ServiceName, ServiceStartupKind.Manual, cancellationToken).ConfigureAwait(false);

        if (service.IsRunning)
        {
            await _serviceManager.StopAsync(ServiceName, cancellationToken).ConfigureAwait(false);
        }

        return changed
            ? (true, "Indexação de busca interrompida e colocada em manual.", 0)
            : (false, "Não foi possível alterar o serviço Windows Search.", 0);
    }

    /// <inheritdoc />
    public override async Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var previous = context.State.TryGetValue($"{Id}.previous", out var value) && value is ServiceStartupKind kind
            ? kind
            : ServiceStartupKind.AutomaticDelayed;

        var restored = await _serviceManager.SetStartupKindAsync(ServiceName, previous, cancellationToken).ConfigureAwait(false);

        if (restored)
        {
            await _serviceManager.StartAsync(ServiceName, cancellationToken).ConfigureAwait(false);
        }

        return new OptimizationStepResult(Id, Name, restored, restored ? "Indexação de busca restaurada." : "Falha ao restaurar o Windows Search.");
    }
}

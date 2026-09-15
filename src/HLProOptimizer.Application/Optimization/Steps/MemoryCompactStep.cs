using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Reduz o working set dos processos em segundo plano (EmptyWorkingSet),
/// devolvendo RAM ao sistema sem encerrar nada.
/// </summary>
public sealed class MemoryCompactStep : OptimizationStepBase
{
    private readonly IProcessService _processService;

    /// <summary>Cria o passo.</summary>
    /// <param name="processService">Serviço de processos.</param>
    /// <param name="logger">Logger.</param>
    public MemoryCompactStep(IProcessService processService, ILogger<MemoryCompactStep> logger)
        : base(logger)
    {
        _processService = processService;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.MemoryCompact;

    /// <inheritdoc />
    public override string Name => "Liberar memória RAM";

    /// <inheritdoc />
    public override string Description => "Compacta o working set dos processos em segundo plano, devolvendo RAM ao sistema.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Quick,
        OptimizationMode.Full,
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override int Order => 60;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        var freed = await _processService.CompactBackgroundMemoryAsync(cancellationToken).ConfigureAwait(false);

        return freed > 0
            ? (true, $"{ByteFormat.Format(freed)} de RAM devolvidos ao sistema.", freed)
            : (true, "Nenhuma memória adicional pôde ser liberada.", 0);
    }
}

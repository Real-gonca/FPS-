using HLProOptimizer.Application.Optimization;
using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>Aplica ajustes de rede de baixa latência (delega ao passo <c>opt.network.latency</c>).</summary>
public sealed class NetworkTweak : StepBackedGameTweak
{
    /// <summary>Cria o tweak.</summary>
    /// <param name="steps">Passos registrados.</param>
    /// <param name="logger">Logger.</param>
    public NetworkTweak(IEnumerable<IOptimizationStep> steps, ILogger<NetworkTweak> logger)
        : base(steps, OptimizationStepIds.NetworkLatency, logger)
    {
    }

    /// <inheritdoc />
    public override string Id => GameTweakCatalog.Ids.Network;
}

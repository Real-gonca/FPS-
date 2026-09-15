using HLProOptimizer.Application.Optimization;
using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>Desativa Game Bar e Game DVR (delega ao passo <c>opt.gamer.gamebar</c>).</summary>
public sealed class GameBarTweak : StepBackedGameTweak
{
    /// <summary>Cria o tweak.</summary>
    /// <param name="steps">Passos registrados.</param>
    /// <param name="logger">Logger.</param>
    public GameBarTweak(IEnumerable<IOptimizationStep> steps, ILogger<GameBarTweak> logger)
        : base(steps, OptimizationStepIds.GameBar, logger)
    {
    }

    /// <inheritdoc />
    public override string Id => GameTweakCatalog.Ids.GameBar;
}

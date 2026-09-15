using HLProOptimizer.Application.Optimization;
using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>Pausa o Windows Update durante os jogos (delega ao passo <c>opt.gamer.wupause</c>).</summary>
public sealed class WindowsUpdateTweak : StepBackedGameTweak
{
    /// <summary>Cria o tweak.</summary>
    /// <param name="steps">Passos registrados.</param>
    /// <param name="logger">Logger.</param>
    public WindowsUpdateTweak(IEnumerable<IOptimizationStep> steps, ILogger<WindowsUpdateTweak> logger)
        : base(steps, OptimizationStepIds.WindowsUpdatePause, logger)
    {
    }

    /// <inheritdoc />
    public override string Id => GameTweakCatalog.Ids.WindowsUpdate;
}

using HLProOptimizer.Application.Optimization;
using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>Otimiza o MMCSS para jogos (delega ao passo <c>opt.gamer.mmcss</c>).</summary>
public sealed class MmcssTweak : StepBackedGameTweak
{
    /// <summary>Cria o tweak.</summary>
    /// <param name="steps">Passos registrados.</param>
    /// <param name="logger">Logger.</param>
    public MmcssTweak(IEnumerable<IOptimizationStep> steps, ILogger<MmcssTweak> logger)
        : base(steps, OptimizationStepIds.Mmcss, logger)
    {
    }

    /// <inheritdoc />
    public override string Id => GameTweakCatalog.Ids.Mmcss;
}

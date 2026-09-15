using HLProOptimizer.Application.Optimization;
using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>Desativa a indexação de busca (delega ao passo <c>opt.gamer.indexing</c>).</summary>
public sealed class IndexingTweak : StepBackedGameTweak
{
    /// <summary>Cria o tweak.</summary>
    /// <param name="steps">Passos registrados.</param>
    /// <param name="logger">Logger.</param>
    public IndexingTweak(IEnumerable<IOptimizationStep> steps, ILogger<IndexingTweak> logger)
        : base(steps, OptimizationStepIds.Indexing, logger)
    {
    }

    /// <inheritdoc />
    public override string Id => GameTweakCatalog.Ids.Indexing;
}

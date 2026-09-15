using HLProOptimizer.Application.Optimization;
using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>Ativa o plano "Desempenho Máximo" (delega ao passo de otimização correspondente).</summary>
public sealed class PowerPlanTweak : StepBackedGameTweak
{
    /// <summary>Cria o tweak.</summary>
    /// <param name="steps">Passos registrados.</param>
    /// <param name="logger">Logger.</param>
    public PowerPlanTweak(IEnumerable<IOptimizationStep> steps, ILogger<PowerPlanTweak> logger)
        : base(steps, OptimizationStepIds.UltimatePerformance, logger)
    {
    }

    /// <inheritdoc />
    public override string Id => GameTweakCatalog.Ids.PowerPlan;
}

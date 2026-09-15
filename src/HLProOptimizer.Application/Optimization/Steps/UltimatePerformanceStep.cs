using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Ativa o plano de energia "Desempenho Máximo" (Ultimate Performance) e aplica
/// os ajustes de USB/throttling. Registra o plano anterior para permitir reversão.
/// </summary>
public sealed class UltimatePerformanceStep : OptimizationStepBase
{
    /// <summary>Chave do estado onde o plano anterior é guardado.</summary>
    public const string PreviousPlanStateKey = "opt.power.previousplan";

    private readonly IPowerPlanService _powerPlanService;

    /// <summary>Cria o passo.</summary>
    /// <param name="powerPlanService">Gerenciador de planos de energia.</param>
    /// <param name="logger">Logger.</param>
    public UltimatePerformanceStep(IPowerPlanService powerPlanService, ILogger<UltimatePerformanceStep> logger)
        : base(logger)
    {
        _powerPlanService = powerPlanService;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.UltimatePerformance;

    /// <inheritdoc />
    public override string Name => "Desempenho máximo";

    /// <inheritdoc />
    public override string Description => "Ativa o plano Ultimate Performance e desativa a suspensão seletiva USB.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Full,
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override int Order => 160;

    /// <inheritdoc />
    public override bool RequiresAdmin => true;

    /// <inheritdoc />
    public override bool IsReversible => true;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.EnableMaximumPerformance)
        {
            return (true, "Desativado nas opções.", 0);
        }

        var previous = await _powerPlanService.GetActivePlanAsync(cancellationToken).ConfigureAwait(false);

        if (previous is not null && !context.State.ContainsKey(PreviousPlanStateKey))
        {
            context.State[PreviousPlanStateKey] = previous.Guid;
        }

        var plan = await _powerPlanService.EnableUltimatePerformanceAsync(cancellationToken).ConfigureAwait(false);

        if (plan is null)
        {
            return (false, "Não foi possível ativar o plano Desempenho Máximo.", 0);
        }

        var usbTweaks = await _powerPlanService.ApplyGamerPowerTweaksAsync(cancellationToken).ConfigureAwait(false);

        return (
            true,
            $"Plano '{plan.Name}' ativado{(usbTweaks ? " + ajustes de USB/throttling aplicados" : string.Empty)}.",
            0);
    }

    /// <inheritdoc />
    public override async Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var previousGuid = context.State.TryGetValue(PreviousPlanStateKey, out var value) ? value as string : null;

        await _powerPlanService.RevertGamerPowerTweaksAsync(cancellationToken).ConfigureAwait(false);
        var restored = await _powerPlanService.RestorePlanAsync(previousGuid, cancellationToken).ConfigureAwait(false);

        return new OptimizationStepResult(
            Id,
            Name,
            restored,
            restored ? $"Plano anterior restaurado ({previousGuid ?? "Equilibrado"})." : "Falha ao restaurar o plano anterior.");
    }
}

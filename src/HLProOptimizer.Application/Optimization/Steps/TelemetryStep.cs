using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>Desativa telemetria e rastreamento do Windows aplicando as recomendações de privacidade.</summary>
public sealed class TelemetryStep : OptimizationStepBase
{
    private readonly IPrivacyService _privacyService;

    /// <summary>Cria o passo.</summary>
    /// <param name="privacyService">Serviço de privacidade.</param>
    /// <param name="logger">Logger.</param>
    public TelemetryStep(IPrivacyService privacyService, ILogger<TelemetryStep> logger)
        : base(logger)
    {
        _privacyService = privacyService;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.Telemetry;

    /// <inheritdoc />
    public override string Name => "Desativar telemetria";

    /// <inheritdoc />
    public override string Description => "Aplica as recomendações de privacidade (DiagTrack, Advertising ID, Activity History, Error Reporting).";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Privacy,
        OptimizationMode.Full
    ];

    /// <inheritdoc />
    public override OptimizationLevel MinimumLevel => OptimizationLevel.Balanced;

    /// <inheritdoc />
    public override int Order => 90;

    /// <inheritdoc />
    public override bool RequiresAdmin => true;

    /// <inheritdoc />
    public override bool IsReversible => true;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.DisableTelemetry)
        {
            return (true, "Desativado nas opções.", 0);
        }

        var result = await _privacyService.ApplyRecommendationsAsync(cancellationToken).ConfigureAwait(false);

        context.State[$"{Id}.result"] = result;

        if (result.RequiresElevation)
        {
            throw new Core.Exceptions.ElevationRequiredException(Name);
        }

        return (
            result.IsSuccess || result.AppliedCount > 0,
            $"Privacidade: {result.Summary}.",
            0);
    }

    /// <inheritdoc />
    public override async Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var result = await _privacyService.RestoreDefaultsAsync(cancellationToken).ConfigureAwait(false);

        return new OptimizationStepResult(Id, Name, result.IsSuccess, $"Padrões restaurados: {result.Summary}.");
    }
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Coloca os efeitos visuais do Windows em "Ajustar para melhor desempenho"
/// (animações, sombras e transparências desligadas).
/// </summary>
public sealed class VisualEffectsStep : OptimizationStepBase
{
    private const string VisualEffectsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string DesktopKey = @"Control Panel\Desktop";
    private const string WindowMetricsKey = @"Control Panel\Desktop\WindowMetrics";

    private readonly IRegistryService _registry;

    /// <summary>Cria o passo.</summary>
    /// <param name="registry">Acesso ao registro.</param>
    /// <param name="logger">Logger.</param>
    public VisualEffectsStep(IRegistryService registry, ILogger<VisualEffectsStep> logger)
        : base(logger)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.VisualEffects;

    /// <inheritdoc />
    public override string Name => "Efeitos visuais em desempenho";

    /// <inheritdoc />
    public override string Description => "Desliga animações, sombras e transparências para reduzir o uso de GPU do shell.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override OptimizationLevel MinimumLevel => OptimizationLevel.Aggressive;

    /// <inheritdoc />
    public override int Order => 140;

    /// <inheritdoc />
    public override bool IsReversible => true;

    /// <inheritdoc />
    protected override Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        var before = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["VisualFXSetting"] = _registry.GetDword(RegistryHiveKind.CurrentUser, VisualEffectsKey, "VisualFXSetting"),
            ["MinAnimate"] = _registry.GetString(RegistryHiveKind.CurrentUser, WindowMetricsKey, "MinAnimate"),
            ["UserPreferencesMask"] = _registry.GetValue(RegistryHiveKind.CurrentUser, DesktopKey, "UserPreferencesMask")
        };

        context.State[$"{Id}.before"] = before;

        // 2 = "Ajustar para obter o melhor desempenho".
        _registry.SetDword(RegistryHiveKind.CurrentUser, VisualEffectsKey, "VisualFXSetting", 2);
        _registry.SetString(RegistryHiveKind.CurrentUser, WindowMetricsKey, "MinAnimate", "0");
        _registry.SetDword(RegistryHiveKind.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0);
        _registry.SetDword(RegistryHiveKind.CurrentUser, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0);

        // Mantém "suavizar fontes" ativo (desligar piora muito a legibilidade).
        _registry.SetString(RegistryHiveKind.CurrentUser, DesktopKey, "FontSmoothing", "2");

        return Task.FromResult((true, "Efeitos visuais ajustados para melhor desempenho.", 0L));
    }

    /// <inheritdoc />
    public override Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        // 0 = "Deixar o Windows escolher" (restaura animações e transparências).
        _registry.SetDword(RegistryHiveKind.CurrentUser, VisualEffectsKey, "VisualFXSetting", 0);
        _registry.SetString(RegistryHiveKind.CurrentUser, WindowMetricsKey, "MinAnimate", "1");
        _registry.SetDword(RegistryHiveKind.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 1);

        return Task.FromResult(new OptimizationStepResult(Id, Name, true, "Efeitos visuais restaurados."));
    }
}

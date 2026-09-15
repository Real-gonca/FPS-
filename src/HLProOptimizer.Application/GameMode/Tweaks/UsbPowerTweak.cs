using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>
/// Desativa a suspensão seletiva de USB e o throttling de processador no plano
/// ativo, eliminando micro-travamentos em mouse, teclado e controladores.
/// </summary>
public sealed class UsbPowerTweak : IGameModeTweak
{
    private readonly IPowerPlanService _powerPlanService;
    private readonly ILogger<UsbPowerTweak> _logger;

    /// <summary>Cria o tweak.</summary>
    /// <param name="powerPlanService">Gerenciador de planos de energia.</param>
    /// <param name="logger">Logger.</param>
    public UsbPowerTweak(IPowerPlanService powerPlanService, ILogger<UsbPowerTweak> logger)
    {
        _powerPlanService = powerPlanService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => GameTweakCatalog.Ids.UsbPower;

    /// <inheritdoc />
    public async Task<bool> ApplyAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var applied = await _powerPlanService.ApplyGamerPowerTweaksAsync(cancellationToken).ConfigureAwait(false);
        context.Log($"[{Id}] suspensão seletiva de USB/throttling {(applied ? "desativados" : "não puderam ser alterados")}.");

        return applied;
    }

    /// <inheritdoc />
    public async Task<bool> RevertAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var reverted = await _powerPlanService.RevertGamerPowerTweaksAsync(cancellationToken).ConfigureAwait(false);
        context.Log($"[{Id}] configurações de energia USB restauradas.");

        _logger.LogDebug("Reversão de energia USB: {Result}.", reverted);

        return reverted;
    }
}

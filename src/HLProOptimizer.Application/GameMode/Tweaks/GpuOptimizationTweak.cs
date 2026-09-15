using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>
/// Otimiza a GPU para desempenho: ativa o Hardware-Accelerated GPU Scheduling (HAGS)
/// e define preferência de GPU de alto desempenho para os jogos do usuário.
/// </summary>
public sealed class GpuOptimizationTweak : IGameModeTweak
{
    private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string UserGpuPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>HAGS: 2 = ativado, 1 = desativado.</summary>
    private const int HwSchModeEnabled = 2;

    private const int HwSchModeDisabled = 1;

    private readonly IRegistryService _registry;
    private readonly ILogger<GpuOptimizationTweak> _logger;

    /// <summary>Cria o tweak.</summary>
    /// <param name="registry">Acesso ao registro.</param>
    /// <param name="logger">Logger.</param>
    public GpuOptimizationTweak(IRegistryService registry, ILogger<GpuOptimizationTweak> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => GameTweakCatalog.Ids.Gpu;

    /// <inheritdoc />
    public Task<bool> ApplyAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var previous = _registry.GetDword(RegistryHiveKind.LocalMachine, GraphicsDriversKey, "HwSchMode");
        context.State[$"{Id}.HwSchMode"] = previous;

        var applied = false;

        try
        {
            _registry.SetDword(RegistryHiveKind.LocalMachine, GraphicsDriversKey, "HwSchMode", HwSchModeEnabled);
            applied = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível ativar o HAGS (requer administrador ou GPU compatível).");
        }

        // Preferência de GPU de alto desempenho para os jogos configurados pelo usuário.
        var games = context.Settings.GameProcessNames;

        foreach (var game in games)
        {
            try
            {
                // "GpuPreference=2;" = alta performance (GPU dedicada).
                _registry.SetString(RegistryHiveKind.CurrentUser, UserGpuPreferencesKey, game, "GpuPreference=2;");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Não foi possível definir a preferência de GPU para {Game}.", game);
            }
        }

        context.Log($"[{Id}] HAGS {(applied ? "ativado (reinicie para aplicar)" : "indisponível")}; {games.Count} jogo(s) com preferência de GPU.");

        return Task.FromResult(applied);
    }

    /// <inheritdoc />
    public Task<bool> RevertAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var previous = context.State.TryGetValue($"{Id}.HwSchMode", out var value) && value is int mode
                ? mode
                : HwSchModeDisabled;

            _registry.SetDword(RegistryHiveKind.LocalMachine, GraphicsDriversKey, "HwSchMode", previous);
            context.Log($"[{Id}] HAGS restaurado (HwSchMode={previous}).");

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao reverter o HAGS.");
            return Task.FromResult(false);
        }
    }
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>
/// Ativa o modo "sem interrupções" (Focus Assist) e desativa toasts durante a
/// sessão de jogo, evitando pop-ups que roubam o foco e causam stuttering.
/// </summary>
public sealed class NotificationsTweak : IGameModeTweak
{
    private const string NotificationsSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string QuietHoursProfileKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\Cache\DefaultAccount";
    private const string GameDvrKey = @"Software\Microsoft\GameBar";

    private readonly IRegistryService _registry;
    private readonly ILogger<NotificationsTweak> _logger;

    /// <summary>Cria o tweak.</summary>
    /// <param name="registry">Acesso ao registro.</param>
    /// <param name="logger">Logger.</param>
    public NotificationsTweak(IRegistryService registry, ILogger<NotificationsTweak> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => GameTweakCatalog.Ids.Notifications;

    /// <inheritdoc />
    public Task<bool> ApplyAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var previousToasts = _registry.GetDword(RegistryHiveKind.CurrentUser, NotificationsSettingsKey, "NOC_GLOBAL_SETTING_TOASTS_ENABLED");
        var previousAllowToasts = _registry.GetDword(RegistryHiveKind.CurrentUser, NotificationsSettingsKey, "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK");

        context.State[$"{Id}.toasts"] = previousToasts;
        context.State[$"{Id}.lockToasts"] = previousAllowToasts;

        try
        {
            // 0 = notificações desativadas globalmente para o usuário.
            _registry.SetDword(RegistryHiveKind.CurrentUser, NotificationsSettingsKey, "NOC_GLOBAL_SETTING_TOASTS_ENABLED", 0);
            _registry.SetDword(RegistryHiveKind.CurrentUser, NotificationsSettingsKey, "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", 0);

            // Focus Assist automático durante jogos/tela cheia.
            _registry.SetDword(RegistryHiveKind.CurrentUser, GameDvrKey, "AutoGameModeEnabled", 1);
            _registry.SetDword(RegistryHiveKind.CurrentUser, QuietHoursProfileKey, "$deactivate$quietHours", 0);

            context.Log($"[{Id}] notificações desativadas (modo sem interrupções).");

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao desativar notificações.");
            context.Log($"[{Id}] falha ao desativar notificações: {ex.Message}");

            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task<bool> RevertAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var toasts = context.State.TryGetValue($"{Id}.toasts", out var t) && t is int tv ? tv : 1;
            var lockToasts = context.State.TryGetValue($"{Id}.lockToasts", out var l) && l is int lv ? lv : 1;

            _registry.SetDword(RegistryHiveKind.CurrentUser, NotificationsSettingsKey, "NOC_GLOBAL_SETTING_TOASTS_ENABLED", toasts);
            _registry.SetDword(RegistryHiveKind.CurrentUser, NotificationsSettingsKey, "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", lockToasts);

            context.Log($"[{Id}] notificações restauradas.");

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao restaurar as notificações.");
            return Task.FromResult(false);
        }
    }
}

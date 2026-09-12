using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Presentation.Notifications;

public sealed class NotificationItem
{
    public NotificationKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    /// <summary>Glyph (Segoe MDL2 Assets) por tipo: info/sucesso/aviso/erro.</summary>
    public string Glyph => Kind switch
    {
        NotificationKind.Success => "\uE73E",
        NotificationKind.Warning => "\uE783",
        NotificationKind.Error => "\uEA39",
        _ => "\uE8FD",
    };
}

/// <summary>
/// Camada de apresentação das notificações (spec §3.2): subscreve o
/// <see cref="INotificationHub"/> da Application, mantém a pilha (máx. 5,
/// glassmorphism subtil) com auto-dismiss configurável.
/// </summary>
public sealed class NotificationCenter
{
    public const int MaxVisible = 5;

    private readonly INotificationHub _hub;
    private readonly ISettingsService _settings;
    private readonly ILogger<NotificationCenter> _log;

    public ObservableCollection<NotificationItem> Items { get; } = new();

    public NotificationCenter(INotificationHub hub, ISettingsService settings, ILogger<NotificationCenter> log)
    {
        _hub = hub;
        _settings = settings;
        _log = log;
        _hub.Emitted += OnHubEmitted;
    }

    private void OnHubEmitted(object? sender, NotificationEmittedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        dispatcher.BeginInvoke(() =>
        {
            var item = new NotificationItem
            {
                Kind = e.Kind,
                Title = e.Title,
                Message = e.Message,
            };
            Items.Add(item);
            while (Items.Count > MaxVisible)
                Items.RemoveAt(0);

            _ = AutoDismissAsync(item);
        });
    }

    private async Task AutoDismissAsync(NotificationItem item)
    {
        int seconds = 6;
        try
        {
            var settings = await _settings.LoadAsync();
            seconds = Math.Max(2, settings.NotificationAutoDismissSeconds);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Não foi possível ler o auto-dismiss; a usar o valor predefinido.");
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
        }
        catch (TaskCanceledException)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        await dispatcher.InvokeAsync(() => Items.Remove(item));
    }
}

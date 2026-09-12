using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application.Notifications;

/// <summary>
/// Hub de notificações da Application (emissor). A Presentation subscreve o
/// evento e apresenta as notificações empilhadas (spec §3.2).
/// </summary>
public sealed class NotificationHub : INotificationHub
{
    public event EventHandler<NotificationEmittedEventArgs>? Emitted;

    public Task EmitAsync(NotificationKind kind, string title, string message, CancellationToken ct = default)
    {
        Emitted?.Invoke(this, new NotificationEmittedEventArgs(kind, title, message, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }
}

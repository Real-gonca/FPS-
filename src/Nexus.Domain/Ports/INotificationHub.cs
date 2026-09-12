using Nexus.Domain.Optimization;

namespace Nexus.Domain.Ports;

public sealed record NotificationEmittedEventArgs(
    NotificationKind Kind,
    string Title,
    string Message,
    DateTimeOffset AtUtc);

/// <summary>
/// Hub de notificações tipadas (info/sucesso/aviso/erro, spec §3.2).
/// A Application emite; a Presentation assina e apresenta (empilhadas,
/// auto-dismiss configurável).
/// </summary>
public interface INotificationHub
{
    event EventHandler<NotificationEmittedEventArgs>? Emitted;

    Task EmitAsync(NotificationKind kind, string title, string message, CancellationToken ct = default);
}

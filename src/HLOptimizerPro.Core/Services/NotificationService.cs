using HLOptimizerPro.Core.Models;

namespace HLOptimizerPro.Core.Services;

/// <summary>Nível visual de uma notificação (usado pelo design system).</summary>
public enum NotificationLevel
{
    Informacao,
    Sucesso,
    Aviso,
    Erro,
}

/// <summary>Notificação apresentada na interface.</summary>
public sealed record AppNotification(
    string Title,
    string Message,
    NotificationLevel Level,
    DateTime Timestamp)
{
    public string IconKind => Level switch
    {
        NotificationLevel.Sucesso => "CheckCircle",
        NotificationLevel.Aviso => "AlertOutline",
        NotificationLevel.Erro => "AlertCircle",
        _ => "InformationOutline",
    };
}

/// <summary>
/// Notificações internas da aplicação.
///
/// Porque não usamos notificações do Windows por omissão? Porque exigem registo de uma AppUserModelID
/// e podem aparecer durante um jogo a ecrã inteiro (o que é exatamente o que tentamos evitar).
/// A UI mostra os avisos dentro da própria janela, e — quando o utilizador ativa as notificações
/// do sistema — delegamos para o serviço da camada de apresentação.
/// </summary>
public sealed class NotificationService
{
    private readonly List<AppNotification> _history = new();
    private const int MaxHistory = 100;

    /// <summary>Notificação emitida (a UI subscreve e anima a entrada).</summary>
    public event EventHandler<AppNotification>? NotificationRaised;

    /// <summary>Histórico recente (painel de notificações da barra de título).</summary>
    public IReadOnlyList<AppNotification> History => _history;

    public void Notify(string title, string message, NotificationLevel level = NotificationLevel.Informacao)
    {
        var notification = new AppNotification(title, message, level, DateTime.Now);

        _history.Insert(0, notification);
        while (_history.Count > MaxHistory) _history.RemoveAt(_history.Count - 1);

        NotificationRaised?.Invoke(this, notification);
    }

    /// <summary>Atalhos semânticos usados pelos ViewModels.</summary>
    public void Success(string title, string message) => Notify(title, message, NotificationLevel.Sucesso);

    public void Warn(string title, string message) => Notify(title, message, NotificationLevel.Aviso);

    public void Error(string title, string message) => Notify(title, message, NotificationLevel.Erro);

    public void Info(string title, string message) => Notify(title, message, NotificationLevel.Informacao);

    /// <summary>Converte um <see cref="Result"/> numa notificação (evita repetição nos ViewModels).</summary>
    public void FromResult(string title, Result result)
        => Notify(title, result.Message, result.Success ? NotificationLevel.Sucesso : NotificationLevel.Erro);

    public void Clear() => _history.Clear();
}

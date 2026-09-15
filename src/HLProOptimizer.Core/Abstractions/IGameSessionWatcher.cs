namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Observador de sessões de jogo: detecta quando um jogo configurado inicia e
/// termina, permitindo ativar/desativar o Modo Gamer automaticamente.
/// </summary>
public interface IGameSessionWatcher
{
    /// <summary>Disparado quando um jogo passa a estar em execução.</summary>
    event EventHandler<string>? GameStarted;

    /// <summary>Disparado quando o jogo em execução é encerrado.</summary>
    event EventHandler<string>? GameStopped;

    /// <summary>Indica se o observador está ativo.</summary>
    bool IsWatching { get; }

    /// <summary>Nome do jogo atualmente detectado (null quando nenhum).</summary>
    string? CurrentGame { get; }

    /// <summary>Inicia a observação (polling leve de processos).</summary>
    void Start();

    /// <summary>Interrompe a observação.</summary>
    void Stop();
}

namespace HLProOptimizer.Application.GameMode;

/// <summary>
/// Um ajuste individual do Modo Gamer, com aplicação e reversão simétricas.
/// </summary>
public interface IGameModeTweak
{
    /// <summary>Identificador do tweak (ex.: "gamer.powerplan").</summary>
    string Id { get; }

    /// <summary>Aplica o ajuste.</summary>
    /// <param name="context">Contexto compartilhado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>True quando aplicado com sucesso.</returns>
    Task<bool> ApplyAsync(GameModeContext context, CancellationToken cancellationToken = default);

    /// <summary>Reverte o ajuste.</summary>
    /// <param name="context">Contexto compartilhado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>True quando revertido (ou quando não havia nada a reverter).</returns>
    Task<bool> RevertAsync(GameModeContext context, CancellationToken cancellationToken = default);
}

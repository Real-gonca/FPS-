namespace Nexus.Domain.Ports;

/// <summary>
/// Elevação de administrador (spec §2.2) com fallback elegante:
/// a app corre asInvoker e degrada para "modo limitado" (leitura + N/D)
/// quando não está elevada; o utilizador pode relançar elevado (UAC) a partir
/// da UI. Se recusar, nada quebra.
/// </summary>
public interface IElevationService
{
    bool IsElevated { get; }

    /// <summary>
    /// Relança a aplicação elevada (runas). true = nova instância iniciada
    /// (a atual deve encerrar); false = UAC recusada/falha (modo limitado).
    /// </summary>
    Task<bool> RequestElevationAsync();
}

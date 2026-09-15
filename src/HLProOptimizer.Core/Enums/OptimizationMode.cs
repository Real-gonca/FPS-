namespace HLProOptimizer.Core.Enums;

/// <summary>
/// Modos de otimização oferecidos na tela "Otimização".
/// Cada modo compõe um conjunto diferente de <c>IOptimizationStep</c>.
/// </summary>
public enum OptimizationMode
{
    /// <summary>Rápida: limpeza básica + flush de DNS. Baixo risco.</summary>
    Quick = 0,

    /// <summary>Completa: rápida + registro + serviços + restauração.</summary>
    Full = 1,

    /// <summary>Gamer: foco em FPS e latência (MMCSS, rede, energia, Game Bar).</summary>
    Gamer = 2,

    /// <summary>Privacidade: telemetria, rastreamento e apps em segundo plano.</summary>
    Privacy = 3
}

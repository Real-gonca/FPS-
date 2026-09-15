namespace HLProOptimizer.Core.Enums;

/// <summary>
/// Nível de agressividade aplicado aos passos de otimização.
/// Configurável em Configurações &gt; Otimização.
/// </summary>
public enum OptimizationLevel
{
    /// <summary>Conservador: apenas itens 100% seguros.</summary>
    Conservative = 0,

    /// <summary>Balanceado: padrão recomendado.</summary>
    Balanced = 1,

    /// <summary>Agressivo: maximiza ganho, pode exigir reconfiguração posterior.</summary>
    Aggressive = 2
}

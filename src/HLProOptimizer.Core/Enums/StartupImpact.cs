namespace HLProOptimizer.Core.Enums;

/// <summary>
/// Impacto estimado de um programa no tempo de boot.
/// Heurística: peso do binário + publisher + histórico de inicialização do Windows.
/// </summary>
public enum StartupImpact
{
    /// <summary>Desconhecido (não foi possível estimar).</summary>
    Unknown = 0,

    /// <summary>Baixo (&lt; 1s).</summary>
    Low = 1,

    /// <summary>Médio (1-3s).</summary>
    Medium = 2,

    /// <summary>Alto (&gt; 3s).</summary>
    High = 3
}

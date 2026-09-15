namespace HLProOptimizer.Core.Enums;

/// <summary>Planos de energia conhecidos do Windows.</summary>
public enum PowerPlanKind
{
    /// <summary>Economia de energia.</summary>
    PowerSaver = 0,

    /// <summary>Equilibrado (padrão).</summary>
    Balanced = 1,

    /// <summary>Alto desempenho.</summary>
    HighPerformance = 2,

    /// <summary>Desempenho máximo (Ultimate Performance).</summary>
    UltimatePerformance = 3,

    /// <summary>Plano personalizado/criado pelo usuário.</summary>
    Custom = 4,

    /// <summary>Plano não reconhecido.</summary>
    Unknown = 5
}

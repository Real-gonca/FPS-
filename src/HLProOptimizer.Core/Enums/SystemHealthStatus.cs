namespace HLProOptimizer.Core.Enums;

/// <summary>Classificação do score geral do sistema.</summary>
public enum SystemHealthStatus
{
    /// <summary>Ruim: score &lt; 40.</summary>
    Poor = 0,

    /// <summary>Regular: 40 &lt;= score &lt; 60.</summary>
    Fair = 1,

    /// <summary>Bom: 60 &lt;= score &lt; 80.</summary>
    Good = 2,

    /// <summary>Excelente: score &gt;= 80.</summary>
    Excellent = 3
}

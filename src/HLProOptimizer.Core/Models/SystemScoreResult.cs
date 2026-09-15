using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>
/// Score do sistema (0-100) exibido no círculo progressivo do Dashboard.
/// Composto por quatro dimensões ponderadas.
/// </summary>
public sealed class SystemScoreResult
{
    /// <summary>Score geral [0-100].</summary>
    public required int OverallScore { get; init; }

    /// <summary>Score de desempenho (CPU, RAM, disco, energia).</summary>
    public required int PerformanceScore { get; init; }

    /// <summary>Score de estabilidade (temperatura, commit, espaço em disco, serviços).</summary>
    public required int StabilityScore { get; init; }

    /// <summary>Score de segurança (Defender, firewall, UAC, updates).</summary>
    public required int SecurityScore { get; init; }

    /// <summary>Score de limpeza (lixo acumulado, registro, inicialização).</summary>
    public required int CleanlinessScore { get; init; }

    /// <summary>Classificação textual do score geral.</summary>
    public SystemHealthStatus Status => OverallScore switch
    {
        >= 80 => SystemHealthStatus.Excellent,
        >= 60 => SystemHealthStatus.Good,
        >= 40 => SystemHealthStatus.Fair,
        _ => SystemHealthStatus.Poor
    };

    /// <summary>Peso de cada dimensão no score geral (para exibir o breakdown).</summary>
    public IReadOnlyDictionary<string, int> Breakdown { get; init; } = new Dictionary<string, int>();

    /// <summary>Justificativas que reduziram o score (exibidas como recomendações).</summary>
    public IReadOnlyList<string> Factors { get; init; } = [];
}

using HLOptimizerPro.Core.Helpers;

namespace HLOptimizerPro.Core.Models;

/// <summary>
/// Uma sub-pontuação da avaliação (Desempenho, Estabilidade, Segurança, Otimização).
/// Cada critério é calculado a partir de medições reais — nunca de valores fictícios.
/// </summary>
public sealed class ScoreBreakdown
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>Pontuação 0–100.</summary>
    public double Score { get; init; }

    /// <summary>Peso relativo no resultado final (0–1, soma dos pesos = 1).</summary>
    public double Weight { get; init; }

    public HealthLevel Health { get; init; }

    /// <summary>Detalhes medidos que justificam a pontuação (mostrados em tooltip/relatório).</summary>
    public IReadOnlyList<ScoreCriterion> Criteria { get; init; } = Array.Empty<ScoreCriterion>();

    public string ScoreText => Formatters.Score(Score);
    public string GradeText => Formatters.ScoreGradeLabel(Score);
}

/// <summary>Critério individual, com o valor medido e a penalização aplicada.</summary>
public sealed class ScoreCriterion
{
    public required string Name { get; init; }
    public required string MeasuredValue { get; init; }
    public double Penalty { get; init; }        // pontos retirados (0–100)
    public bool IsProblem => Penalty >= 5;
    public string? Advice { get; init; }
}

/// <summary>Relatório completo de avaliação do sistema.</summary>
public sealed class AssessmentReport
{
    public DateTime GeneratedAt { get; init; } = DateTime.Now;

    /// <summary>Pontuação global 0–100 (média ponderada das sub-pontuações).</summary>
    public double OverallScore { get; init; }

    public string Grade { get; init; } = "Regular";

    public IReadOnlyList<ScoreBreakdown> Breakdowns { get; init; } = Array.Empty<ScoreBreakdown>();

    /// <summary>Diagnóstico em linguagem natural, derivado dos problemas encontrados.</summary>
    public string Diagnosis { get; init; } = string.Empty;

    /// <summary>Recomendações geradas pela avaliação (alimentam a tela Recomendações).</summary>
    public IReadOnlyList<Recommendation> Recommendations { get; init; } = Array.Empty<Recommendation>();

    /// <summary>Contexto de hardware/OS no momento da avaliação (para o relatório exportado).</summary>
    public HardwareInfo? Hardware { get; init; }

    public string OverallScoreText => Formatters.Score(OverallScore);
}

/// <summary>Recomendação acionável mostrada na tela Recomendações.</summary>
public sealed class Recommendation
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public RecommendationPriority Priority { get; init; } = RecommendationPriority.Media;
    public RecommendationCategory Category { get; init; } = RecommendationCategory.Desempenho;

    /// <summary>Ganho estimado, descrito qualitativamente e com base no problema medido.</summary>
    public string ExpectedBenefit { get; init; } = string.Empty;

    /// <summary>Chave da ação que o botão "Aplicar" executa (resolvida pelo OptimizationCatalog).</summary>
    public string? ActionKey { get; init; }

    public bool IsApplied { get; set; }

    /// <summary>Ícone (Material Design Icons) para o item de lista.</summary>
    public string IconKind { get; init; } = "Speedometer";

    public string PriorityText => Priority switch
    {
        RecommendationPriority.Alta => "Prioridade alta",
        RecommendationPriority.Media => "Prioridade média",
        _ => "Prioridade baixa",
    };

    public string CategoryText => Category switch
    {
        RecommendationCategory.Desempenho => "Desempenho",
        RecommendationCategory.Memoria => "Memória",
        RecommendationCategory.Rede => "Rede",
        RecommendationCategory.Armazenamento => "Armazenamento",
        RecommendationCategory.Seguranca => "Segurança",
        RecommendationCategory.Inicializacao => "Inicialização",
        RecommendationCategory.Graficos => "Gráficos",
        _ => "Sistema",
    };
}

/// <summary>Item da lista de programas de arranque (critério de avaliação + Otimizações).</summary>
public sealed class StartupItem
{
    public required string Name { get; init; }
    public string Publisher { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;   // Chave de registo / pasta Startup
    public bool Enabled { get; init; }
    public bool RecommendedToDisable { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool IsMicrosoft { get; init; }
}

/// <summary>Estado do antivírus/segurança (WMI SecurityCenter2).</summary>
public sealed class SecurityStatus
{
    public bool AntivirusEnabled { get; init; }
    public bool RealTimeProtection { get; init; }
    public string AntivirusName { get; init; } = "Não detetado";
    public bool FirewallEnabled { get; init; }
    public bool WindowsUpdateRecent { get; init; }
    public int PendingUpdates { get; init; }
    public DateTime? LastUpdateDate { get; init; }
    public bool DefenderPresent { get; init; }
    public string Notes { get; init; } = string.Empty;

    /// <summary>True quando não há proteção ativa — estado crítico na avaliação.</summary>
    public bool IsVulnerable => !AntivirusEnabled || !FirewallEnabled;
}

/// <summary>Informação do sistema operativo (ecrã Configuração / PC Boost).</summary>
public sealed class SystemReport
{
    public string OsName { get; init; } = "Windows";
    public string OsVersion { get; init; } = string.Empty;
    public string OsBuild { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public bool IsElevated { get; init; }
    public bool IsWindows11 { get; init; }
    public int LogicalProcessors { get; init; }
    public DateTime BootTime { get; init; }
    public TimeSpan Uptime => DateTime.Now - BootTime;
    public bool SecureBootEnabled { get; init; }
    public bool VirtualizationEnabled { get; init; }
    public string PowerPlan { get; init; } = "Desconhecido";
    public string DotNetVersion { get; init; } = "8.0";
}

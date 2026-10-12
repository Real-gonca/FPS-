namespace HLOptimizerPro.Core.Models;

/// <summary>
/// Todos os dados medidos que alimentam a avaliação do sistema.
///
/// Esta separação (medição → cálculo) tem duas vantagens concretas:
///  1. O cálculo da pontuação é uma função pura, fácil de testar (ver <c>ScoringServiceTests</c>).
///  2. Nenhum valor é inventado: se um dado não foi medido, fica no valor por omissão e o
///     critério correspondente é apresentado como "não medido" em vez de penalizar/beneficiar
///     o utilizador com base em suposições.
/// </summary>
public sealed class AssessmentInput
{
    // ---- Desempenho -----------------------------------------------------------
    public double CpuUsagePercent { get; init; }
    public double RamUsagePercent { get; init; }
    public double DiskFreePercent { get; init; }
    public string StorageType { get; init; } = "Desconhecido";
    public string PowerPlan { get; init; } = "Desconhecido";
    public int StartupEnabledCount { get; init; }
    public int StartupTotalCount { get; init; }
    public double GpuUsagePercent { get; init; }

    // ---- Estabilidade ---------------------------------------------------------
    public TimeSpan Uptime { get; init; }
    public double? CpuTemperatureCelsius { get; init; }
    public int ProblemDevices { get; init; }
    public bool DiskHealthy { get; init; } = true;
    public int DiskHealthUnknown { get; init; }
    public bool WindowsUpdateRecent { get; init; } = true;
    public double? FragmentationPercent { get; init; }
    public int CriticalServiceStopped { get; init; }

    // ---- Segurança ------------------------------------------------------------
    public bool AntivirusEnabled { get; init; }
    public bool RealTimeProtection { get; init; }
    public bool FirewallEnabled { get; init; }
    public bool SecureBootEnabled { get; init; }
    public bool UacEnabled { get; init; } = true;
    public int PendingUpdates { get; init; }
    public bool SmartScreenEnabled { get; init; } = true;

    // ---- Otimização -----------------------------------------------------------
    public int AppliedOptimizations { get; init; }
    public int TotalAvailableOptimizations { get; init; }
    public bool TelemetryReduced { get; init; }
    public bool StorageSenseEnabled { get; init; }
    public bool GameModeConfigured { get; init; }
    public bool DisabledStartupItems { get; init; }
    public bool VisualEffectsOptimized { get; init; }

    /// <summary>Contexto usado apenas no cabeçalho do relatório (não entra na pontuação).</summary>
    public HardwareInfo? Hardware { get; init; }

    /// <summary>Programas de arranque com impacto alto ainda ativos (alimenta recomendações).</summary>
    public IReadOnlyList<string> HighImpactStartupItems { get; init; } = Array.Empty<string>();
}

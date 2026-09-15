namespace HLProOptimizer.Core.Models;

/// <summary>
/// Entradas usadas pelo cálculo do Score do Sistema. Mantido como tipo puro para
/// permitir testes unitários determinísticos do <c>ISystemScoreCalculator</c>.
/// </summary>
public sealed record ScoreInput
{
    /// <summary>Uso de CPU [0-100].</summary>
    public double CpuUsagePercent { get; init; }

    /// <summary>Uso de memória [0-100].</summary>
    public double MemoryUsagePercent { get; init; }

    /// <summary>Percentual de espaço livre no volume do sistema [0-100].</summary>
    public double SystemDiskFreePercent { get; init; }

    /// <summary>Temperatura da CPU em °C (null quando indisponível).</summary>
    public double? CpuTemperatureCelsius { get; init; }

    /// <summary>Temperatura da GPU em °C (null quando indisponível).</summary>
    public double? GpuTemperatureCelsius { get; init; }

    /// <summary>Espaço recuperável identificado pela análise, em bytes.</summary>
    public long JunkBytes { get; init; }

    /// <summary>Entradas órfãs/inválidas encontradas no registro.</summary>
    public int OrphanedRegistryEntries { get; init; }

    /// <summary>Programas de inicialização ativos.</summary>
    public int EnabledStartupPrograms { get; init; }

    /// <summary>Programas de inicialização de alto impacto ativos.</summary>
    public int HighImpactStartupPrograms { get; init; }

    /// <summary>Itens de privacidade ainda não protegidos.</summary>
    public int UnprotectedPrivacyItems { get; init; }

    /// <summary>Total de itens de privacidade monitorados.</summary>
    public int TotalPrivacyItems { get; init; }

    /// <summary>Antivírus registrado e ativo.</summary>
    public bool AntivirusActive { get; init; } = true;

    /// <summary>Firewall do Windows ativo.</summary>
    public bool FirewallActive { get; init; } = true;

    /// <summary>UAC habilitado.</summary>
    public bool UacEnabled { get; init; } = true;

    /// <summary>Atualizações do Windows em dia (sem pendências antigas).</summary>
    public bool WindowsUpToDate { get; init; } = true;

    /// <summary>Serviços essenciais parados indevidamente.</summary>
    public int CriticalServicesStopped { get; init; }

    /// <summary>Uso de commit charge [0-100].</summary>
    public double CommitChargePercent { get; init; }

    /// <summary>Drivers com problema.</summary>
    public int ProblemDrivers { get; init; }

    /// <summary>Uptime em dias (reboot recente é sinal de manutenção).</summary>
    public double UptimeDays { get; init; }

    /// <summary>Plano de energia de alto desempenho ativo.</summary>
    public bool HighPerformancePowerPlan { get; init; }

    /// <summary>Total de memória física em bytes (para ponderar o peso do lixo).</summary>
    public long TotalMemoryBytes { get; init; }
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Application.Scoring;

/// <summary>
/// Implementação determinística do Score do Sistema (0-100).
/// </summary>
/// <remarks>
/// <para>
/// O score é composto por quatro dimensões independentes, cada uma calculada a
/// partir de penalidades acumuladas sobre uma base 100:
/// </para>
/// <list type="bullet">
///   <item><description><b>Desempenho (35%)</b> - CPU, RAM, espaço em disco, plano de energia e peso da inicialização.</description></item>
///   <item><description><b>Limpeza (25%)</b> - lixo acumulado, registro órfão, programas de inicialização e privacidade.</description></item>
///   <item><description><b>Estabilidade (20%)</b> - temperaturas, commit charge, uptime, serviços críticos e drivers.</description></item>
///   <item><description><b>Segurança (20%)</b> - antivírus, firewall, UAC e Windows Update.</description></item>
/// </list>
/// <para>
/// Penalidades são progressivas (não lineares) para que um único problema grave
/// derrube o score de forma visível, enquanto vários problemas pequenos se somem.
/// A classe é pura: não toca Windows, WMI nem disco - por isso é 100% testável.
/// </para>
/// </remarks>
public sealed class SystemScoreCalculator : ISystemScoreCalculator
{
    /// <summary>Peso da dimensão de desempenho no score geral.</summary>
    public const double PerformanceWeight = 0.35d;

    /// <summary>Peso da dimensão de limpeza no score geral.</summary>
    public const double CleanlinessWeight = 0.25d;

    /// <summary>Peso da dimensão de estabilidade no score geral.</summary>
    public const double StabilityWeight = 0.20d;

    /// <summary>Peso da dimensão de segurança no score geral.</summary>
    public const double SecurityWeight = 0.20d;

    // Limiares de desempenho
    private const double CpuComfortThreshold = 65d;
    private const double MemoryComfortThreshold = 75d;
    private const double DiskFreeComfortThreshold = 20d;
    private const double CommitComfortThreshold = 70d;

    // Limiares térmicos
    private const double CpuWarmCelsius = 70d;
    private const double CpuHotCelsius = 85d;
    private const double GpuWarmCelsius = 75d;
    private const double GpuHotCelsius = 88d;

    /// <inheritdoc />
    public SystemScoreResult Calculate(ScoreInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var factors = new List<string>();

        var performance = CalculatePerformanceScore(input, factors);
        var cleanliness = CalculateCleanlinessScore(input, factors);
        var stability = CalculateStabilityScore(input, factors);
        var security = CalculateSecurityScore(input, factors);

        var overall = (int)Math.Round(
            (performance * PerformanceWeight) +
            (cleanliness * CleanlinessWeight) +
            (stability * StabilityWeight) +
            (security * SecurityWeight),
            MidpointRounding.AwayFromZero);

        overall = Math.Clamp(overall, 0, 100);

        return new SystemScoreResult
        {
            OverallScore = overall,
            PerformanceScore = performance,
            StabilityScore = stability,
            SecurityScore = security,
            CleanlinessScore = cleanliness,
            Breakdown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desempenho"] = performance,
                ["Limpeza"] = cleanliness,
                ["Estabilidade"] = stability,
                ["Segurança"] = security
            },
            Factors = factors
        };
    }

    /// <summary>Calcula a dimensão de desempenho (CPU, RAM, disco, energia, boot).</summary>
    private static int CalculatePerformanceScore(ScoreInput input, ICollection<string> factors)
    {
        var penalty = 0d;

        // CPU: penaliza progressivamente acima do limiar de conforto.
        if (input.CpuUsagePercent > CpuComfortThreshold)
        {
            var excess = input.CpuUsagePercent - CpuComfortThreshold;
            var cpuPenalty = Math.Min(30d, excess * 0.9d);
            penalty += cpuPenalty;

            if (cpuPenalty > 12d)
            {
                factors.Add($"Uso de CPU elevado ({input.CpuUsagePercent:F0}%) está reduzindo a capacidade de resposta.");
            }
        }

        // Memória: acima de 75% o Windows começa a paginar com frequência.
        if (input.MemoryUsagePercent > MemoryComfortThreshold)
        {
            var excess = input.MemoryUsagePercent - MemoryComfortThreshold;
            var memoryPenalty = Math.Min(28d, excess * 1.15d);
            penalty += memoryPenalty;

            if (memoryPenalty > 10d)
            {
                factors.Add($"Memória RAM em {input.MemoryUsagePercent:F0}% - considere fechar aplicativos em segundo plano.");
            }
        }

        // Commit charge alto indica pressão de memória virtual.
        if (input.CommitChargePercent > CommitComfortThreshold)
        {
            penalty += Math.Min(10d, (input.CommitChargePercent - CommitComfortThreshold) * 0.35d);
        }

        // Espaço livre no volume do sistema.
        if (input.SystemDiskFreePercent < DiskFreeComfortThreshold)
        {
            var deficit = DiskFreeComfortThreshold - input.SystemDiskFreePercent;
            var diskPenalty = Math.Min(30d, deficit * 1.6d);
            penalty += diskPenalty;

            if (input.SystemDiskFreePercent < 10d)
            {
                factors.Add($"Apenas {input.SystemDiskFreePercent:F1}% do disco do sistema está livre - o Windows precisa de folga para paginação e atualizações.");
            }
        }

        // Plano de energia: equilíbrio não explora o hardware em jogos.
        if (!input.HighPerformancePowerPlan)
        {
            penalty += 5d;
            factors.Add("O plano de energia ativo não é de alto desempenho.");
        }

        // Itens de inicialização de alto impacto pesam no boot e no idle.
        if (input.HighImpactStartupPrograms > 0)
        {
            penalty += Math.Min(12d, input.HighImpactStartupPrograms * 3d);
            factors.Add($"{input.HighImpactStartupPrograms} programa(s) de inicialização de alto impacto ativo(s).");
        }
        else if (input.EnabledStartupPrograms > 12)
        {
            penalty += Math.Min(8d, (input.EnabledStartupPrograms - 12) * 0.8d);
        }

        return Score(penalty);
    }

    /// <summary>Calcula a dimensão de limpeza (lixo, registro, privacidade).</summary>
    private static int CalculateCleanlinessScore(ScoreInput input, ICollection<string> factors)
    {
        var penalty = 0d;

        // Lixo acumulado: normalizado pela memória total (máquina com 8 GB sofre
        // muito mais com 4 GB de lixo do que uma com 64 GB).
        if (input.JunkBytes > 0)
        {
            var reference = input.TotalMemoryBytes > 0 ? input.TotalMemoryBytes : 16L * 1024 * 1024 * 1024;
            var ratio = input.JunkBytes / (double)reference;
            var junkPenalty = Math.Min(40d, ratio * 55d);
            penalty += junkPenalty;

            if (junkPenalty > 10d)
            {
                factors.Add($"{ByteFormat.Format(input.JunkBytes)} de arquivos desnecessários podem ser liberados.");
            }
        }

        // Registro: entradas órfãs degradam a resolução de caminhos.
        if (input.OrphanedRegistryEntries > 0)
        {
            penalty += Math.Min(18d, Math.Log2(input.OrphanedRegistryEntries + 1) * 3.2d);

            if (input.OrphanedRegistryEntries > 25)
            {
                factors.Add($"{input.OrphanedRegistryEntries} entradas inválidas no registro do Windows.");
            }
        }

        // Privacidade: proporção de itens ainda não protegidos.
        if (input.TotalPrivacyItems > 0 && input.UnprotectedPrivacyItems > 0)
        {
            var unprotectedRatio = input.UnprotectedPrivacyItems / (double)input.TotalPrivacyItems;
            var privacyPenalty = Math.Min(30d, unprotectedRatio * 34d);
            penalty += privacyPenalty;

            if (unprotectedRatio > 0.5d)
            {
                factors.Add($"{input.UnprotectedPrivacyItems} de {input.TotalPrivacyItems} itens de privacidade continuam expostos.");
            }
        }

        // Excesso de programas na inicialização (mesmo sem alto impacto).
        if (input.EnabledStartupPrograms > 20)
        {
            penalty += Math.Min(8d, (input.EnabledStartupPrograms - 20) * 0.6d);
        }

        return Score(penalty);
    }

    /// <summary>Calcula a dimensão de estabilidade (temperatura, uptime, serviços, drivers).</summary>
    private static int CalculateStabilityScore(ScoreInput input, ICollection<string> factors)
    {
        var penalty = 0d;

        if (input.CpuTemperatureCelsius is { } cpuTemp)
        {
            if (cpuTemp >= CpuHotCelsius)
            {
                penalty += Math.Min(45d, 25d + ((cpuTemp - CpuHotCelsius) * 2.5d));
                factors.Add($"CPU a {cpuTemp:F0}°C - risco de thermal throttling e perda de FPS.");
            }
            else if (cpuTemp >= CpuWarmCelsius)
            {
                penalty += Math.Min(20d, (cpuTemp - CpuWarmCelsius) * 1.4d);
                factors.Add($"CPU a {cpuTemp:F0}°C - verifique fluxo de ar e pasta térmica.");
            }
        }

        if (input.GpuTemperatureCelsius is { } gpuTemp)
        {
            if (gpuTemp >= GpuHotCelsius)
            {
                penalty += Math.Min(30d, 18d + ((gpuTemp - GpuHotCelsius) * 2d));
                factors.Add($"GPU a {gpuTemp:F0}°C - acima do ideal para sessões longas de jogo.");
            }
            else if (gpuTemp >= GpuWarmCelsius)
            {
                penalty += Math.Min(14d, (gpuTemp - GpuWarmCelsius) * 1.1d);
            }
        }

        if (input.CriticalServicesStopped > 0)
        {
            penalty += Math.Min(40d, input.CriticalServicesStopped * 15d);
            factors.Add($"{input.CriticalServicesStopped} serviço(s) essencial(is) do Windows estão parados.");
        }

        if (input.ProblemDrivers > 0)
        {
            penalty += Math.Min(20d, input.ProblemDrivers * 7d);
            factors.Add($"{input.ProblemDrivers} dispositivo(s) com driver em estado de erro.");
        }

        // Uptime muito longo acumula vazamentos de memória e fragmentação de pool.
        if (input.UptimeDays > 14d)
        {
            penalty += Math.Min(10d, (input.UptimeDays - 14d) * 0.6d);
            factors.Add($"O sistema está ligado há {input.UptimeDays:F0} dia(s) sem reiniciar.");
        }

        return Score(penalty);
    }

    /// <summary>Calcula a dimensão de segurança.</summary>
    private static int CalculateSecurityScore(ScoreInput input, ICollection<string> factors)
    {
        var penalty = 0d;

        if (!input.AntivirusActive)
        {
            penalty += 45d;
            factors.Add("Nenhum antivírus ativo foi detectado.");
        }

        if (!input.FirewallActive)
        {
            penalty += 30d;
            factors.Add("O Firewall do Windows está desativado em ao menos um perfil de rede.");
        }

        if (!input.UacEnabled)
        {
            penalty += 20d;
            factors.Add("O Controle de Conta de Usuário (UAC) está desativado.");
        }

        if (!input.WindowsUpToDate)
        {
            penalty += 12d;
            factors.Add("Há atualizações do Windows pendentes ou vencidas.");
        }

        return Score(penalty);
    }

    /// <summary>Converte uma penalidade acumulada em um score inteiro [0-100].</summary>
    private static int Score(double penalty)
        => Math.Clamp((int)Math.Round(100d - penalty, MidpointRounding.AwayFromZero), 0, 100);
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Application.Scoring;

/// <summary>
/// Monta um <see cref="ScoreInput"/> a partir dos dados que o aplicativo já coletou.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que um builder separado?</b> O cálculo do score é puro
/// (<see cref="SystemScoreCalculator"/>), mas alimentar os ~20 campos dele exige
/// combinar perfil de hardware, amostra de desempenho, relatório de análise,
/// privacidade e inicialização. Concentrar esse mapeamento aqui:
/// </para>
/// <list type="bullet">
///   <item>evita duplicação entre Dashboard e outras telas (DRY);</item>
///   <item>mantém a lógica testável sem WMI nem WPF;</item>
///   <item>tolera dados ausentes — cada parâmetro é opcional e o builder degrada
///   graciosamente (ex.: sem relatório, os campos de limpeza ficam zerados e o
///   score reflete apenas o que foi medido).</item>
/// </list>
/// </remarks>
public static class ScoreInputBuilder
{
    /// <summary>Constrói a entrada do score.</summary>
    /// <param name="profile">Perfil do hardware/SO (obrigatório).</param>
    /// <param name="sample">Amostra de desempenho mais recente (opcional).</param>
    /// <param name="report">Relatório da última análise (opcional).</param>
    /// <param name="privacyItems">Itens de privacidade conhecidos (opcional).</param>
    /// <param name="startupPrograms">Programas de inicialização (opcional).</param>
    /// <param name="gameMode">Estado do Modo Gamer (opcional).</param>
    /// <param name="activePowerPlan">Plano de energia ativo (opcional).</param>
    /// <param name="antivirusActive">Antivírus registrado e ativo.</param>
    /// <param name="firewallActive">Firewall do Windows ativo.</param>
    /// <param name="uacEnabled">UAC habilitado.</param>
    /// <returns>Entrada pronta para <see cref="ISystemScoreCalculator.Calculate"/>.</returns>
    public static ScoreInput Build(
        SystemProfile profile,
        PerformanceSample? sample = null,
        ScanReport? report = null,
        IReadOnlyCollection<PrivacyItem>? privacyItems = null,
        IReadOnlyCollection<StartupProgram>? startupPrograms = null,
        GameModeState? gameMode = null,
        PowerPlanInfo? activePowerPlan = null,
        bool antivirusActive = true,
        bool firewallActive = true,
        bool uacEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var issues = report?.Issues ?? [];

        return new ScoreInput
        {
            CpuUsagePercent = sample?.CpuPercent ?? 0d,
            MemoryUsagePercent = sample?.MemoryPercent ?? profile.Memory.UsedPercent,
            SystemDiskFreePercent = ResolveSystemDiskFreePercent(profile, sample),
            CpuTemperatureCelsius = sample?.CpuTemperatureCelsius,
            GpuTemperatureCelsius = sample?.GpuTemperatureCelsius,
            JunkBytes = report?.TotalRecoverableBytes ?? 0,
            OrphanedRegistryEntries = CountItems(issues, IssueCategory.Registry),
            EnabledStartupPrograms = startupPrograms is { Count: > 0 }
                ? startupPrograms.Count(p => p.IsEnabled)
                : CountItems(issues, IssueCategory.StartupPrograms),
            HighImpactStartupPrograms = startupPrograms?
                .Count(p => p.IsEnabled && p.Impact == StartupImpact.High) ?? 0,
            UnprotectedPrivacyItems = privacyItems?.Count(p => !p.IsProtected) ?? 0,
            TotalPrivacyItems = privacyItems?.Count ?? 0,
            AntivirusActive = antivirusActive && !HasSeriousIssue(issues, IssueCategory.Security),
            FirewallActive = firewallActive,
            UacEnabled = uacEnabled,
            WindowsUpToDate = !HasSeriousIssue(issues, IssueCategory.WindowsUpdate),
            CriticalServicesStopped = CountItems(issues, IssueCategory.Services, Severity.High),
            CommitChargePercent = ResolveCommitChargePercent(profile.Memory),
            ProblemDrivers = CountItems(issues, IssueCategory.Drivers),
            UptimeDays = profile.OperatingSystem.Uptime.TotalDays,
            HighPerformancePowerPlan = IsHighPerformance(activePowerPlan) || (gameMode?.IsActive ?? false),
            TotalMemoryBytes = profile.Memory.TotalBytes
        };
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    /// <summary>
    /// Espaço livre do volume do sistema.
    /// Prefere o dado real da partição; na falta dele, inverte o uso medido na amostra.
    /// </summary>
    private static double ResolveSystemDiskFreePercent(SystemProfile profile, PerformanceSample? sample)
    {
        var partition = profile.SystemPartition;

        if (partition is { SizeBytes: > 0 })
        {
            return Percentage.Of(partition.FreeBytes, partition.SizeBytes);
        }

        return sample is null ? 0d : Math.Max(0d, 100d - sample.DiskPercent);
    }

    /// <summary>Commit charge em percentual (0 quando o limite é desconhecido).</summary>
    private static double ResolveCommitChargePercent(MemoryInfo memory) =>
        memory.CommitLimitBytes > 0
            ? Percentage.Of(memory.CommitChargeBytes, memory.CommitLimitBytes)
            : 0d;

    /// <summary>Plano de energia de alto desempenho (inclui "Desempenho Máximo").</summary>
    private static bool IsHighPerformance(PowerPlanInfo? plan) =>
        plan is not null &&
        plan.Kind is PowerPlanKind.HighPerformance or PowerPlanKind.UltimatePerformance;

    /// <summary>Soma <see cref="AnalysisIssue.ItemCount"/> de uma categoria.</summary>
    /// <param name="issues">Problemas do relatório.</param>
    /// <param name="category">Categoria desejada.</param>
    /// <param name="minimumSeverity">Severidade mínima considerada.</param>
    private static int CountItems(
        IReadOnlyList<AnalysisIssue> issues,
        IssueCategory category,
        Severity minimumSeverity = Severity.Low) =>
        issues
            .Where(i => i.Category == category && i.Severity >= minimumSeverity)
            .Sum(i => Math.Max(i.ItemCount, i.RecoverableBytes > 0 ? 1 : 0));

    /// <summary>True quando há um problema relevante na categoria (afeta segurança/atualizações).</summary>
    private static bool HasSeriousIssue(IReadOnlyList<AnalysisIssue> issues, IssueCategory category) =>
        issues.Any(i => i.Category == category && i.Severity >= Severity.Medium);
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Application.Analysis.Rules;

/// <summary>
/// Avalia o desempenho geral: espaço em disco, memória, plano de energia,
/// temperatura e uptime.
/// </summary>
public sealed class PerformanceAnalysisRule : IAnalysisRule
{
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly IPowerPlanService _powerPlanService;

    /// <summary>Cria a regra.</summary>
    /// <param name="performanceMonitor">Monitor em tempo real (uma amostra avulsa é suficiente).</param>
    /// <param name="powerPlanService">Gerenciador de planos de energia.</param>
    public PerformanceAnalysisRule(IPerformanceMonitor performanceMonitor, IPowerPlanService powerPlanService)
    {
        _performanceMonitor = performanceMonitor;
        _powerPlanService = powerPlanService;
    }

    /// <inheritdoc />
    public string Id => "analysis.performance";

    /// <inheritdoc />
    public string Name => "Verificar desempenho do sistema";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Performance;

    /// <inheritdoc />
    public int Order => 90;

    /// <inheritdoc />
    public bool RequiresAdmin => false;

    /// <inheritdoc />
    public double Weight => 1.2d;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnalysisIssue>> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<AnalysisIssue>();
        var profile = context.Profile;

        // ---------------- Espaço em disco ----------------
        var systemDisk = profile.SystemPartition;

        if (systemDisk is not null && systemDisk.Health != DiskHealth.Healthy)
        {
            var severity = systemDisk.Health == DiskHealth.Critical ? Severity.Critical : Severity.High;

            issues.Add(new AnalysisIssue(
                Id: "analysis.performance.diskspace",
                Title: $"Pouco espaço livre em {systemDisk.DeviceId}",
                Description: $"A unidade {systemDisk.DeviceId} tem apenas {ByteFormat.Format(systemDisk.FreeBytes)} livres ({systemDisk.FreePercent:F1}%). O Windows precisa de espaço para paginação, hibernação e atualizações.",
                Category: Category,
                Severity: severity,
                RecoverableBytes: (context.CleanupTargets ?? []).Sum(t => t.EstimatedBytes),
                RecommendedAction: "Limpar disco",
                CanAutoFix: true,
                ItemCount: 1,
                FixIdentifier: Optimization.OptimizationStepIds.TemporaryFiles));
        }

        // ---------------- Memória ----------------
        var sample = await _performanceMonitor.ReadSampleAsync(cancellationToken).ConfigureAwait(false);

        if (sample.MemoryPercent > 85d)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.performance.memory",
                Title: "Uso de memória elevado",
                Description: $"A memória está em {sample.MemoryPercent:F0}% ({ByteFormat.Format(sample.MemoryInUseBytes)} de {ByteFormat.Format(profile.Memory.TotalBytes)}). Fechar processos em segundo plano libera RAM para os jogos.",
                Category: Category,
                Severity: Severity.High,
                RecommendedAction: "Liberar memória",
                CanAutoFix: true,
                ItemCount: 1,
                FixIdentifier: Optimization.OptimizationStepIds.MemoryCompact));
        }
        else if (sample.MemoryPercent > 75d)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.performance.memory",
                Title: "Memória sob pressão moderada",
                Description: $"Uso de memória em {sample.MemoryPercent:F0}%. Ativar o Modo Gamer libera RAM e reduz processos em segundo plano.",
                Category: Category,
                Severity: Severity.Medium,
                RecommendedAction: "Liberar memória",
                CanAutoFix: true,
                ItemCount: 1,
                FixIdentifier: Optimization.OptimizationStepIds.MemoryCompact));
        }

        // ---------------- Temperatura ----------------
        if (sample.CpuTemperatureCelsius is { } cpuTemp && cpuTemp >= 85d)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.performance.cputemp",
                Title: "Temperatura da CPU alta",
                Description: $"A CPU está a {cpuTemp:F0}°C. Acima de 85°C o processador reduz o clock (thermal throttling), causando quedas de FPS. Verifique cooler, fluxo de ar e pasta térmica.",
                Category: Category,
                Severity: cpuTemp >= 92d ? Severity.Critical : Severity.High,
                RecommendedAction: "Verificar refrigeração",
                CanAutoFix: false));
        }

        if (sample.GpuTemperatureCelsius is { } gpuTemp && gpuTemp >= 85d)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.performance.gputemp",
                Title: "Temperatura da GPU alta",
                Description: $"A GPU está a {gpuTemp:F0}°C. Limpe as ventoinhas e verifique a curva de fans para evitar throttling em jogos.",
                Category: Category,
                Severity: gpuTemp >= 90d ? Severity.Critical : Severity.High,
                RecommendedAction: "Verificar refrigeração",
                CanAutoFix: false));
        }

        // ---------------- Plano de energia ----------------
        var activePlan = await _powerPlanService.GetActivePlanAsync(cancellationToken).ConfigureAwait(false);

        if (activePlan is not null && activePlan.Kind is PowerPlanKind.Balanced or PowerPlanKind.PowerSaver)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.performance.powerplan",
                Title: $"Plano de energia '{activePlan.Name}' limita o desempenho",
                Description: "Planos equilibrados/econômicos reduzem o clock da CPU e ativam a suspensão seletiva de USB, aumentando a latência de entrada em jogos.",
                Category: Category,
                Severity: Severity.Medium,
                RecommendedAction: "Ativar desempenho máximo",
                CanAutoFix: true,
                RequiresAdmin: true,
                ItemCount: 1,
                FixIdentifier: Optimization.OptimizationStepIds.UltimatePerformance));
        }

        // ---------------- Uptime ----------------
        var uptime = profile.OperatingSystem.Uptime;

        if (uptime.TotalDays > 14d)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.performance.uptime",
                Title: "Sistema sem reiniciar há muito tempo",
                Description: $"O Windows está ligado há {(int)uptime.TotalDays} dia(s). Reinicializações liberam memória não paginável acumulada por drivers e serviços.",
                Category: Category,
                Severity: Severity.Low,
                RecommendedAction: "Reiniciar o PC",
                CanAutoFix: false));
        }

        // ---------------- VRAM ----------------
        var gpu = profile.PrimaryGpu;

        if (gpu is { AdapterRamBytes: > 0 and < 4L * 1024 * 1024 * 1024 })
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.performance.vram",
                Title: "VRAM limitada para jogos atuais",
                Description: $"A GPU '{gpu.Name}' possui {ByteFormat.Format(gpu.AdapterRamBytes)} de VRAM. Reduza texturas e desative ray tracing para manter FPS estável.",
                Category: Category,
                Severity: Severity.Informational,
                RecommendedAction: "Ajustar gráficos do jogo",
                CanAutoFix: false));
        }

        return issues;
    }
}

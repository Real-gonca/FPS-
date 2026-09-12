using System.Globalization;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Metrics;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application.Recommendations;

/// <summary>
/// Motor de recomendações (spec §4.1): cada regra dispara SOMENTE com dados
/// reais presentes; impacto é uma estimativa transparente (a regra está
/// documentada aqui e nos testes), nunca uma medição — a medição A/B chega
/// com o benchmark (patch de Otimização Rápida).
/// </summary>
public sealed class RecommendationsEngine
{
    /// <summary>Regra 2 dispara quando os temporários superam 300 MB.</summary>
    public const double TempCleanupThresholdBytes = 300.0 * 1024 * 1024;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    private readonly ILogger<RecommendationsEngine> _log;

    public RecommendationsEngine(ILogger<RecommendationsEngine> log) => _log = log;

    public IReadOnlyList<Recommendation> Build(
        SystemTelemetrySnapshot snap,
        StorageProbeResult? tempProbe,
        bool advancedMode)
    {
        var list = new List<Recommendation>();

        // Regra 1 — Telemetria de base (sempre disponível; baixa, reversível, documentada).
        list.Add(new Recommendation(
            TaskKeys.TelemetryDisable,
            "Desativar telemetria de base",
            "Define a política AllowTelemetry=0 em HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection, " +
            "reduzindo a recolha de dados de diagnóstico do Windows (CEIP/Feedback Hub). Não afeta o Windows " +
            "Update, a segurança nem o diagnóstico de rede. Reversível por rollback (o valor anterior é guardado).",
            ImpactPercent: 15,
            Risk: RiskLevel.Low,
            Reversible: true,
            IsAdvisory: false,
            Visibility: FeatureVisibility.Simple));

        // Regra 2 — Limpeza de temporários (só com varrimento real > 300 MB).
        if (tempProbe is { TotalBytes: > TempCleanupThresholdBytes })
        {
            double gb = tempProbe.TotalBytes.Value / (1024.0 * 1024 * 1024);
            int impact = (int)Math.Clamp(Math.Ceiling(gb * 60), 5, 40);
            if (snap.DiskFreePercent.IsAvailable && snap.DiskFreePercent.Value < 10)
                impact = Math.Min(50, impact + 10);

            string approx = tempProbe.TimedOut ? " (aprox. — varrimento com limite de tempo)" : string.Empty;
            list.Add(new Recommendation(
                TaskKeys.CleanupTemp,
                "Limpar ficheiros temporários",
                $"~{gb.ToString("0.0", Pt)} GB recuperáveis em {tempProbe.RootPath}{approx}. " +
                "A eliminação terá preview obrigatório (módulo Limpeza Avançada, patch 4).",
                impact,
                RiskLevel.Low,
                Reversible: false,
                IsAdvisory: false,
                Visibility: FeatureVisibility.Simple));
        }

        // Regra 3 — Pressão de RAM (apenas Modo Avançado; ação implica terminar processos).
        if (snap.RamFreePercent.IsAvailable && snap.RamFreePercent.Value < 10)
        {
            list.Add(new Recommendation(
                TaskKeys.RamTrim,
                "Reduzir consumo de memória de fundo",
                "Lista os processos com maior consumo de RAM para decidir quais terminar. " +
                "Sem 'boost' mágico — vê exatamente o que vai acontecer antes de agir.",
                ImpactPercent: 20,
                Risk: RiskLevel.Medium,
                Reversible: false,
                IsAdvisory: false,
                Visibility: FeatureVisibility.Advanced));
        }

        // Regra 4 — Temperatura elevada (informativa; não aplica nada).
        if (snap.CpuTemperatureC.IsAvailable && snap.CpuTemperatureC.Value > 85)
        {
            list.Add(new Recommendation(
                TaskKeys.AdvisoryThermal,
                "Temperatura elevada detetada",
                $"A zona térmica reporta {snap.CpuTemperatureC.Value.ToString("0.0", Pt)} °C. " +
                "Verifique ventilação/refrigeração antes de cargas intensas. Recomendação informativa — nenhuma ação é aplicada.",
                ImpactPercent: 0,
                Risk: RiskLevel.High,
                Reversible: true,
                IsAdvisory: true,
                Visibility: FeatureVisibility.Simple));
        }

        _log.LogDebug("Recomendações geradas: {Count} (modo {Mode}).", list.Count, advancedMode ? "Avançado" : "Simples");

        return list
            .Where(r => ModeFilter.IsVisible(r.Visibility, advancedMode))
            .OrderByDescending(r => r.ImpactPercent)
            .ThenBy(r => r.Risk)
            .ToList();
    }
}

/// <summary>Chaves canónicas das tarefas (evitam strings mágicas espalhadas).</summary>
public static class TaskKeys
{
    public const string TelemetryDisable = "telemetry-disable";
    public const string CleanupTemp = "cleanup-temp";
    public const string RamTrim = "ram-trim";
    public const string AdvisoryThermal = "advisory-thermal";
}

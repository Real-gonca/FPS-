using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using Xunit;

namespace HLOptimizerPro.Tests;

/// <summary>
/// Testes da pontuação do sistema.
///
/// O ScoringService é uma função pura (medições → relatório), o que torna estes testes rápidos e
/// determinísticos. O objetivo não é fixar números mágicos: é garantir as PROPRIEDADES do cálculo
/// (limites, pesos, monotonia, coerência das recomendações), que são o que o utilizador sente.
/// </summary>
public sealed class ScoringServiceTests
{
    private readonly ScoringService _scoring = new();

    /// <summary>
    /// Construtor de cenários: começa num sistema saudável e permite piorar apenas o que o teste
    /// quer avaliar. Assim cada teste é legível e muda exatamente uma variável.
    /// </summary>
    private static AssessmentInput Build(
        double cpu = 12,
        double ram = 38,
        double diskFree = 45,
        string storage = "SSD NVMe",
        string powerPlan = "Alto desempenho",
        int startupEnabled = 4,
        int startupTotal = 12,
        double uptimeHours = 6,
        double? temperature = 48,
        int problemDevices = 0,
        bool diskHealthy = true,
        bool updateRecent = true,
        double? fragmentation = 2,
        int criticalServicesStopped = 0,
        bool antivirus = true,
        bool realTimeProtection = true,
        bool firewall = true,
        bool secureBoot = true,
        bool uac = true,
        int pendingUpdates = 0,
        bool smartScreen = true,
        int appliedOptimizations = 30,
        int totalOptimizations = 30,
        bool telemetryReduced = true,
        bool storageSense = true,
        bool gameModeConfigured = true,
        bool startupManaged = true,
        bool visualEffectsOptimized = true)
        => new()
        {
            CpuUsagePercent = cpu,
            RamUsagePercent = ram,
            DiskFreePercent = diskFree,
            StorageType = storage,
            PowerPlan = powerPlan,
            StartupEnabledCount = startupEnabled,
            StartupTotalCount = startupTotal,
            GpuUsagePercent = 8,

            Uptime = TimeSpan.FromHours(uptimeHours),
            CpuTemperatureCelsius = temperature,
            ProblemDevices = problemDevices,
            DiskHealthy = diskHealthy,
            WindowsUpdateRecent = updateRecent,
            FragmentationPercent = fragmentation,
            CriticalServiceStopped = criticalServicesStopped,

            AntivirusEnabled = antivirus,
            RealTimeProtection = realTimeProtection,
            FirewallEnabled = firewall,
            SecureBootEnabled = secureBoot,
            UacEnabled = uac,
            PendingUpdates = pendingUpdates,
            SmartScreenEnabled = smartScreen,

            AppliedOptimizations = appliedOptimizations,
            TotalAvailableOptimizations = totalOptimizations,
            TelemetryReduced = telemetryReduced,
            StorageSenseEnabled = storageSense,
            GameModeConfigured = gameModeConfigured,
            DisabledStartupItems = startupManaged,
            VisualEffectsOptimized = visualEffectsOptimized,
        };

    [Fact]
    public void Sistema_saudavel_tem_pontuacao_alta_e_classificacao_excelente()
    {
        var report = _scoring.Calculate(Build());

        Assert.True(report.OverallScore >= 85,
            $"Esperava >= 85 num sistema saudável, mas obteve {report.OverallScore:0.0}.");
        Assert.Equal("Excelente", report.Grade);
        Assert.DoesNotContain(report.Breakdowns, b => b.Health == HealthLevel.Critico);
    }

    [Fact]
    public void Sistema_problematico_tem_pontuacao_baixa_recomendacoes_e_diagnostico()
    {
        var report = _scoring.Calculate(Build(
            cpu: 88, ram: 94, diskFree: 3, storage: "HDD", powerPlan: "Economia de energia",
            startupEnabled: 24, startupTotal: 26, uptimeHours: 24 * 21, temperature: 91,
            problemDevices: 3, diskHealthy: false, updateRecent: false, fragmentation: 22,
            criticalServicesStopped: 2, antivirus: false, realTimeProtection: false, firewall: false,
            secureBoot: false, uac: false, pendingUpdates: 7, smartScreen: false,
            appliedOptimizations: 0, telemetryReduced: false, storageSense: false,
            gameModeConfigured: false, startupManaged: false, visualEffectsOptimized: false));

        Assert.True(report.OverallScore <= 40,
            $"Esperava <= 40 num sistema problemático, mas obteve {report.OverallScore:0.0}.");
        Assert.Contains(report.Grade, new[] { "Crítico", "Regular" });
        Assert.NotEmpty(report.Recommendations);
        Assert.False(string.IsNullOrWhiteSpace(report.Diagnosis));
    }

    [Fact]
    public void Pontuacao_e_penalizacoes_estao_sempre_entre_0_e_100()
    {
        var scenarios = new[]
        {
            Build(),
            Build(cpu: 100, ram: 100, diskFree: 0, startupEnabled: 99, startupTotal: 99,
                uptimeHours: 24 * 400, temperature: 120, problemDevices: 50, fragmentation: 100,
                criticalServicesStopped: 20, antivirus: false, realTimeProtection: false,
                firewall: false, secureBoot: false, uac: false, pendingUpdates: 100,
                smartScreen: false, appliedOptimizations: 0, telemetryReduced: false,
                storageSense: false, gameModeConfigured: false, startupManaged: false,
                visualEffectsOptimized: false),
            new AssessmentInput(),   // nada medido: não pode explodir
        };

        foreach (var input in scenarios)
        {
            var report = _scoring.Calculate(input);

            Assert.InRange(report.OverallScore, 0, 100);

            foreach (var breakdown in report.Breakdowns)
            {
                Assert.InRange(breakdown.Score, 0, 100);

                foreach (var criterion in breakdown.Criteria)
                    Assert.InRange(criterion.Penalty, 0, 100);
            }
        }
    }

    [Fact]
    public void Pesos_das_quatro_areas_somam_um_e_estao_identificados()
    {
        var report = _scoring.Calculate(Build());

        Assert.Equal(4, report.Breakdowns.Count);
        Assert.Equal(1.0, report.Breakdowns.Sum(b => b.Weight), precision: 3);

        Assert.Contains(report.Breakdowns, b => b.Name == "Desempenho" && Math.Abs(b.Weight - 0.30) < 0.001);
        Assert.Contains(report.Breakdowns, b => b.Name == "Estabilidade" && Math.Abs(b.Weight - 0.25) < 0.001);
        Assert.Contains(report.Breakdowns, b => b.Name == "Segurança" && Math.Abs(b.Weight - 0.25) < 0.001);
        Assert.Contains(report.Breakdowns, b => b.Name == "Otimização" && Math.Abs(b.Weight - 0.20) < 0.001);
    }

    [Fact]
    public void Melhorar_apenas_a_seguranca_aumenta_a_pontuacao()
    {
        var insecure = Build(antivirus: false, realTimeProtection: false, firewall: false,
            secureBoot: false, uac: false, pendingUpdates: 7, smartScreen: false);

        var secure = Build();

        var insecureReport = _scoring.Calculate(insecure);
        var secureReport = _scoring.Calculate(secure);

        Assert.True(secureReport.OverallScore > insecureReport.OverallScore,
            "Ativar antivírus, firewall, Secure Boot, UAC e SmartScreen tem de aumentar a pontuação.");
    }

    [Fact]
    public void Piorar_apenas_o_arranque_baixa_a_area_de_desempenho()
    {
        var good = _scoring.Calculate(Build(startupEnabled: 3, startupTotal: 14));
        var bad = _scoring.Calculate(Build(startupEnabled: 20, startupTotal: 22));

        var goodPerformance = good.Breakdowns.First(b => b.Name == "Desempenho");
        var badPerformance = bad.Breakdowns.First(b => b.Name == "Desempenho");

        Assert.True(badPerformance.Score < goodPerformance.Score,
            "Mais programas de arranque ativos tem de baixar a sub-pontuação de Desempenho.");
    }

    [Fact]
    public void Criterios_sao_auditaveis_e_problemas_tem_conselho()
    {
        var report = _scoring.Calculate(Build(
            ram: 95, diskFree: 4, startupEnabled: 25, startupTotal: 25, temperature: 93,
            antivirus: false, realTimeProtection: false, firewall: false, appliedOptimizations: 2));

        var criteria = report.Breakdowns.SelectMany(b => b.Criteria).ToList();
        Assert.NotEmpty(criteria);

        Assert.All(criteria, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.MeasuredValue));
        });

        var problems = criteria.Where(c => c.IsProblem).ToList();
        Assert.NotEmpty(problems);
        Assert.All(problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Advice)));
    }

    [Fact]
    public void Recomendacoes_nao_se_repetem_e_as_chaves_tem_formato_valido()
    {
        var report = _scoring.Calculate(Build(
            ram: 92, startupEnabled: 22, startupTotal: 24, diskFree: 6, antivirus: false,
            firewall: false, appliedOptimizations: 1, telemetryReduced: false));

        var ids = report.Recommendations.Select(r => r.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        var withAction = report.Recommendations.Where(r => r.ActionKey is not null).ToList();
        Assert.NotEmpty(withAction);

        // As chaves seguem o formato "area.tweak", o mesmo usado no catálogo de otimizações.
        Assert.All(withAction, r => Assert.Contains('.', r.ActionKey!));
        Assert.All(report.Recommendations, r => Assert.False(string.IsNullOrWhiteSpace(r.ExpectedBenefit)));
    }
}

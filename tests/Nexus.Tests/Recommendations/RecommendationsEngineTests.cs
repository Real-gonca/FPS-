using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Application.Recommendations;
using Nexus.Domain.Ports;
using Nexus.Tests.Fakes;
using Xunit;

namespace Nexus.Tests.Recommendations;

/// <summary>
/// Regras do motor de recomendações: só disparam com dados reais, respeitam o
/// Modo Simples/Avançado e ficam ordenadas por impacto.
/// </summary>
public class RecommendationsEngineTests
{
    private readonly RecommendationsEngine _engine = new(NullLogger<RecommendationsEngine>.Instance);

    [Fact]
    public void AlwaysOffers_TelemetryTask_LowRisk_Reversible()
    {
        var recs = _engine.Build(SnapshotFacts.Standard(), null, advancedMode: false);

        var rec = Assert.Single(recs, r => r.TaskKey == TaskKeys.TelemetryDisable);
        Assert.Equal(Nexus.Domain.Optimization.RiskLevel.Low, rec.Risk);
        Assert.True(rec.Reversible);
        Assert.False(rec.IsAdvisory);
    }

    [Fact]
    public void TempBiggerThanThreshold_OffersCleanup_WithApproxLabelWhenTimedOut()
    {
        var probe = new StorageProbeResult(1.5 * 1024 * 1024 * 1024, TimedOut: true, @"C:\Users\dev\AppData\Local\Temp");
        var recs = _engine.Build(SnapshotFacts.Standard(), probe, advancedMode: false);

        var rec = Assert.Single(recs, r => r.TaskKey == TaskKeys.CleanupTemp);
        Assert.Contains("aprox.", rec.Description);
        Assert.Contains(@"C:\Users\dev\AppData\Local\Temp", rec.Description);
    }

    [Fact]
    public void TempBelowThreshold_DoesNotOfferCleanup()
    {
        var probe = new StorageProbeResult(100 * 1024 * 1024, TimedOut: false, @"C:\Temp");
        var recs = _engine.Build(SnapshotFacts.Standard(), probe, advancedMode: false);

        Assert.DoesNotContain(recs, r => r.TaskKey == TaskKeys.CleanupTemp);
    }

    [Fact]
    public void NoProbeResult_DoesNotOfferCleanup()
    {
        var recs = _engine.Build(SnapshotFacts.Standard(), tempProbe: null, advancedMode: false);

        Assert.DoesNotContain(recs, r => r.TaskKey == TaskKeys.CleanupTemp);
    }

    [Fact]
    public void RamUnderPressure_OffersTrim_OnlyInAdvancedMode()
    {
        var underPressure = SnapshotFacts.Standard(ramFreeMb: 500); // ~3% livres

        var inSimple = _engine.Build(underPressure, null, advancedMode: false);
        var inAdvanced = _engine.Build(underPressure, null, advancedMode: true);

        Assert.DoesNotContain(inSimple, r => r.TaskKey == TaskKeys.RamTrim);
        Assert.Contains(inAdvanced, r => r.TaskKey == TaskKeys.RamTrim);
    }

    [Fact]
    public void HighTemperature_AddsAdvisory_WithoutTask()
    {
        var hot = SnapshotFacts.Standard(temperature: 92);
        var recs = _engine.Build(hot, null, advancedMode: false);

        var rec = Assert.Single(recs, r => r.TaskKey == TaskKeys.AdvisoryThermal);
        Assert.True(rec.IsAdvisory);
    }

    [Fact]
    public void NormalTemperature_NoAdvisory()
    {
        var recs = _engine.Build(SnapshotFacts.Standard(temperature: 45), null, advancedMode: false);

        Assert.DoesNotContain(recs, r => r.TaskKey == TaskKeys.AdvisoryThermal);
    }

    [Fact]
    public void ResultsAreSorted_ByImpactDescending()
    {
        var probe = new StorageProbeResult(2.0 * 1024 * 1024 * 1024, TimedOut: false, @"C:\Temp");
        var underPressure = SnapshotFacts.Standard(ramFreeMb: 500);
        var recs = _engine.Build(underPressure, probe, advancedMode: true);

        var impacts = recs.Select(r => r.ImpactPercent).ToList();
        Assert.Equal(impacts.OrderByDescending(i => i), impacts);
    }
}

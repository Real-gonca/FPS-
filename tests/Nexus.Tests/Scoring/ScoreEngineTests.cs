using Nexus.Application.Scoring;
using Nexus.Tests.Fakes;
using Xunit;

namespace Nexus.Tests.Scoring;

/// <summary>
/// Regras do ScoreEngine: pesos transparentes, reponderação quando há N/D,
/// e score N/D quando não há nenhuma métrica real.
/// </summary>
public class ScoreEngineTests
{
    private readonly ScoreEngine _engine = new();

    [Fact]
    public void AllMetricsAvailable_ProducesExpectedScore()
    {
        // disco 60% (30) + RAM 40% (30) + CPU 70 (20) + temp 100 (20)
        // = (60*30 + 40*30 + 70*20 + 100*20) / 100 = 64
        var result = _engine.ComputeScore(SnapshotFacts.Standard());

        Assert.Equal(64, result.Score);
        Assert.All(result.Factors, f => Assert.True(f.Available));
    }

    [Fact]
    public void TemperatureMissing_RenormalizesWeights()
    {
        // sem temperatura: (60*30 + 40*30 + 70*20) / 80 = 55
        var result = _engine.ComputeScore(SnapshotFacts.Standard(temperature: null));

        Assert.Equal(55, result.Score);
        var tempFactor = result.Factors.First(f => f.Name == "Temperatura");
        Assert.False(tempFactor.Available);
        Assert.Contains("reponderados", result.Summary);
    }

    [Fact]
    public void AllMetricsMissing_ReturnsNullScore()
    {
        var snapshot = new Nexus.Domain.Metrics.SystemTelemetrySnapshot(
            DateTimeOffset.UtcNow,
            Nexus.Domain.Metrics.MetricValue.Missing(),
            Nexus.Domain.Metrics.MetricValue.Missing(),
            Nexus.Domain.Metrics.MetricValue.Missing(),
            Nexus.Domain.Metrics.MetricValue.Missing(),
            Nexus.Domain.Metrics.MetricValue.Missing(),
            Nexus.Domain.Metrics.MetricValue.Missing(),
            Nexus.Domain.Metrics.MetricValue.Missing(),
            Nexus.Domain.Metrics.MetricValue.Missing(),
            null,
            null);

        var result = _engine.ComputeScore(snapshot);

        Assert.Null(result.Score);
        Assert.Contains("N/D", result.Summary);
    }

    [Fact]
    public void MoreDiskFree_MonotonicallyImprovesScore()
    {
        var tight = _engine.ComputeScore(SnapshotFacts.Standard(diskFreeGb: 25.6));   // 5% livre
        var roomy = _engine.ComputeScore(SnapshotFacts.Standard(diskFreeGb: 460.8));  // 90% livre

        Assert.True(roomy.Score > tight.Score);
    }

    [Fact]
    public void ScoreIsClampedToZeroHundred()
    {
        var worst = _engine.ComputeScore(SnapshotFacts.Standard(cpu: 100, temperature: 120, diskFreeGb: 0, ramFreeMb: 0));
        var best = _engine.ComputeScore(SnapshotFacts.Standard(cpu: 0, temperature: 30, diskFreeGb: 512, ramFreeMb: 16384));

        Assert.Equal(0, worst.Score);
        Assert.Equal(100, best.Score);
    }

    [Fact]
    public void CpuFactor_IsInverseOfUsage()
    {
        var idle = _engine.ComputeScore(SnapshotFacts.Standard(cpu: 0));
        var busy = _engine.ComputeScore(SnapshotFacts.Standard(cpu: 100));

        Assert.True(idle.Score > busy.Score);
        var idleCpu = idle.Factors.First(f => f.Name == "Pressão de CPU");
        Assert.Equal(100, idleCpu.FactorValue);
    }
}

using Nexus.Domain.Metrics;
using Xunit;

namespace Nexus.Tests.Domain;

/// <summary>Regra de ouro: sem fonte real → N/D, nunca um número inventado.</summary>
public class MetricValueTests
{
    [Fact]
    public void Missing_FormatsAsND()
    {
        var value = MetricValue.Missing();

        Assert.False(value.IsAvailable);
        Assert.Equal("N/D", value.Format());
        Assert.Equal("N/D", value.Format("0.0"));
    }

    [Fact]
    public void Of_FormatsWithPtPtCulture()
    {
        var value = MetricValue.Of(1234.56);

        Assert.True(value.IsAvailable);
        Assert.Equal("1.234,6", value.Format("0.0"));
        Assert.Equal("1.235", value.Format("0"));
    }

    [Fact]
    public void Of_Zero_IsAvailable()
    {
        Assert.True(MetricValue.Of(0).IsAvailable);
        Assert.Equal("0", MetricValue.Of(0).Format());
    }

    [Fact]
    public void Snapshot_PercentsAreND_WhenAnyPartMissing()
    {
        var snap = new SystemTelemetrySnapshot(
            DateTimeOffset.UtcNow,
            MetricValue.Of(10),
            MetricValue.Of(100),
            MetricValue.Missing(),
            MetricValue.Of(200),
            MetricValue.Missing(),
            MetricValue.Of(500),
            MetricValue.Missing(),
            MetricValue.Missing(),
            null,
            null);

        Assert.Equal("N/D", snap.DiskFreePercent.Format());
        Assert.Equal("N/D", snap.RamFreePercent.Format());
        Assert.Equal("N/D", snap.RamUsedPercent.Format());
    }
}

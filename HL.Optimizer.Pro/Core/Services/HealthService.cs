using HL.Optimizer.Core.Interfaces;
using HL.Optimizer.Core.Models;
namespace HL.Optimizer.Core.Services;
public sealed class HealthService(ISystemProbe probe)
{
    public async Task<IReadOnlyList<Finding>> ScanAsync(CancellationToken token)
    {
        var metrics = await probe.ReadAsync(token); var findings = new List<Finding>();
        foreach (var m in metrics.Where(m => m.Percent.HasValue))
        {
            var high = m.Percent >= (m.Name == "Storage" ? 90 : 85);
            findings.Add(new(m.Name, m.Value + " · " + m.Detail, high ? "Review resource usage; this is a sample, not proof of a persistent problem." : "No pressure detected in this sample.", high ? "Attention" : "Normal"));
        }
        findings.Add(new("Scan coverage", "CPU, memory and system-volume usage", "Windows Update, driver freshness and system integrity are not verified by this scan. Use Windows Tools for supported diagnostics.", "Information"));
        return findings;
    }
}

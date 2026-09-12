namespace Nexus.Domain.Ports;

/// <summary>
/// Registo persistente de micro-benchmarks (base para os relatórios do
/// patch 7 — os números A/B ficam auditáveis).
/// </summary>
public interface IBenchmarkLog
{
    /// <param name="context">Ex.: "quick-before", "quick-after" — rastreia a medição.</param>
    Task SaveAsync(BenchmarkResult result, string context = "", CancellationToken ct = default);

    /// <summary>Mais recentes primeiro.</summary>
    Task<IReadOnlyList<BenchmarkResult>> GetRecentAsync(int take = 50, CancellationToken ct = default);
}

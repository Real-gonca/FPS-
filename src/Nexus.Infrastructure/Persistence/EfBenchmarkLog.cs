using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Persistence;

public sealed class EfBenchmarkLog : ScopedDbAccess, IBenchmarkLog
{
    public EfBenchmarkLog(IServiceScopeFactory scopes, ILogger<EfBenchmarkLog> log)
        : base(scopes, log)
    {
    }

    public Task SaveAsync(BenchmarkResult result, string context = "", CancellationToken ct = default) =>
        WithDbAsync(async db =>
        {
            db.Benchmarks.Add(new BenchmarkRow
            {
                TimestampUtc = result.TimestampUtc,
                CpuIndex = result.CpuIndex,
                MemAllocMbPerSec = result.MemAllocMbPerSec,
                DiskReadMbPerSec = result.DiskReadMbPerSec,
                DiskWriteMbPerSec = result.DiskWriteMbPerSec,
                DurationMs = result.DurationMs,
                Context = context,
            });
            await db.SaveChangesAsync(ct);
        }, ct);

    public Task<IReadOnlyList<BenchmarkResult>> GetRecentAsync(int take = 50, CancellationToken ct = default) =>
        WithDbAsync(async db =>
        {
            var rows = await db.Benchmarks
                .OrderByDescending(b => b.TimestampUtc)
                .Take(take)
                .ToListAsync(ct);
            return rows.Select(r => new BenchmarkResult(
                r.TimestampUtc, r.CpuIndex, r.MemAllocMbPerSec,
                r.DiskReadMbPerSec, r.DiskWriteMbPerSec, r.DurationMs)).ToList();
        }, ct);
}

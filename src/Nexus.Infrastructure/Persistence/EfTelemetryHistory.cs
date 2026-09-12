using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Persistence;

public sealed class EfTelemetryHistory : ScopedDbAccess, ITelemetryHistory
{
    /// <summary>Retenção das amostras (suficiente para janelas de horas/dias).</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public EfTelemetryHistory(IServiceScopeFactory scopes, ILogger<EfTelemetryHistory> log)
        : base(scopes, log)
    {
    }

    public Task SaveSampleAsync(TelemetrySample sample, CancellationToken ct = default) =>
        WithDbAsync(async db =>
        {
            db.TelemetrySamples.Add(new TelemetrySampleRow
            {
                TimestampUtc = sample.TimestampUtc,
                CpuPercent = sample.CpuPercent,
                RamUsedPercent = sample.RamUsedPercent,
                TemperatureC = sample.TemperatureC,
                DiskFreePercent = sample.DiskFreePercent,
            });

            // Purge barato (delete por query, sem materializar): mantém a base pequena.
            DateTimeOffset cutoff = DateTimeOffset.UtcNow - Retention;
            await db.TelemetrySamples
                .Where(s => s.TimestampUtc < cutoff)
                .ExecuteDeleteAsync(ct);

            await db.SaveChangesAsync(ct);
        }, ct);

    public Task<IReadOnlyList<TelemetrySample>> GetRecentAsync(int hours, CancellationToken ct = default) =>
        WithDbAsync(async db =>
        {
            DateTimeOffset since = DateTimeOffset.UtcNow.AddHours(-Math.Max(1, hours));
            var rows = await db.TelemetrySamples
                .Where(s => s.TimestampUtc >= since)
                .OrderBy(s => s.TimestampUtc)
                .ToListAsync(ct);

            return rows.Select(r => new TelemetrySample(
                r.TimestampUtc,
                r.CpuPercent,
                r.RamUsedPercent,
                r.TemperatureC,
                r.DiskFreePercent)).ToList();
        }, ct);
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Persistence;

public sealed class EfHistoryStore : ScopedDbAccess, IHistoryStore
{
    public EfHistoryStore(IServiceScopeFactory scopes, ILogger<EfHistoryStore> log)
        : base(scopes, log)
    {
    }

    public Task AddAsync(OptimizationAction action, CancellationToken ct = default) =>
        WithDbAsync(async db =>
        {
            db.Actions.Add(ToRow(action));
            await db.SaveChangesAsync(ct);
        }, ct);

    public Task UpdateAsync(OptimizationAction action, CancellationToken ct = default) =>
        WithDbAsync(async db =>
        {
            var row = await db.Actions.FindAsync(new object[] { action.Id }, ct);
            if (row is null)
            {
                Log.LogWarning("UpdateAsync: ação {Id} não existe no histórico.", action.Id);
                return;
            }

            row.Status = action.Status.ToString();
            row.ResultSummary = action.ResultSummary;
            row.FinishedUtc = action.FinishedUtc;
            row.ElapsedMs = action.ElapsedMs;
            row.BackupIds = JsonSerializer.Serialize(action.BackupIds);
            await db.SaveChangesAsync(ct);
        }, ct);

    public Task<OptimizationAction?> FindAsync(Guid id, CancellationToken ct = default) =>
        WithDbAsync(async db =>
        {
            var row = await db.Actions.FindAsync(new object[] { id }, ct);
            return row is null ? null : FromRow(row);
        }, ct);

    public Task<IReadOnlyList<OptimizationAction>> QueryAsync(int take = 100, CancellationToken ct = default) =>
        WithDbAsync(async db =>
        {
            var rows = await db.Actions
                .OrderByDescending(a => a.StartedUtc)
                .Take(take)
                .ToListAsync(ct);
            return rows.Select(FromRow).ToList();
        }, ct);

    internal static OptimizationActionRow ToRow(OptimizationAction action) => new()
    {
        Id = action.Id,
        TaskKey = action.TaskKey,
        Name = action.Name,
        Description = action.Description,
        Risk = action.Risk.ToString(),
        Status = action.Status.ToString(),
        ResultSummary = action.ResultSummary,
        StartedUtc = action.StartedUtc,
        FinishedUtc = action.FinishedUtc,
        ElapsedMs = action.ElapsedMs,
        BackupIds = JsonSerializer.Serialize(action.BackupIds),
    };

    internal static OptimizationAction FromRow(OptimizationActionRow row)
    {
        List<Guid> backups;
        try
        {
            backups = JsonSerializer.Deserialize<List<Guid>>(row.BackupIds) ?? new List<Guid>();
        }
        catch
        {
            backups = new List<Guid>();
        }

        return new OptimizationAction
        {
            Id = row.Id,
            TaskKey = row.TaskKey,
            Name = row.Name,
            Description = row.Description,
            Risk = Enum.TryParse<RiskLevel>(row.Risk, out var risk) ? risk : RiskLevel.Low,
            Status = Enum.TryParse<OptimizationStatus>(row.Status, out var status) ? status : OptimizationStatus.Failed,
            ResultSummary = row.ResultSummary,
            StartedUtc = row.StartedUtc,
            FinishedUtc = row.FinishedUtc,
            ElapsedMs = row.ElapsedMs,
            BackupIds = backups,
        };
    }
}

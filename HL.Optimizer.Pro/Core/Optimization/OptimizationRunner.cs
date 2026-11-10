using HL.Optimizer.Core.Interfaces;
using HL.Optimizer.Core.Models;
namespace HL.Optimizer.Core.Optimization;
/// <summary>Write-ahead backup, serialized writes, verification, compensation and crash recovery.</summary>
public sealed class OptimizationRunner(IOperationJournal journal)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task ApplyAsync(IReversibleSetting setting, bool consent, CancellationToken token)
    {
        if (!consent) throw new InvalidOperationException("Explicit consent required.");
        await gate.WaitAsync(token);
        try
        {
            var records = await journal.ReadAsync(token);
            if (records.Any(r => r.TaskId == setting.Definition.Id && r.Status == "Prepared" && !records.Any(x => x.Id == r.Id && x.Status == "Restored")))
                throw new InvalidOperationException("Restore the previous change before applying again.");
            var before = setting.Read(); var id = Guid.NewGuid();
            await journal.AppendAsync(new(id, DateTimeOffset.Now, setting.Definition.Id, "Prepared", setting.Target, before), token);
            try
            {
                setting.Write(setting.Target);
                if (setting.Read() != setting.Target) throw new IOException("Verification failed.");
                await journal.AppendAsync(new(id, DateTimeOffset.Now, setting.Definition.Id, "Applied", "Verified", before));
            }
            catch
            {
                try
                {
                    setting.Write(before);
                    if (setting.Read() != before) throw new IOException("Rollback verification failed.");
                    await journal.AppendAsync(new(id, DateTimeOffset.Now, setting.Definition.Id, "Restored", "Compensated after failure", before));
                }
                catch (Exception rollback)
                { await journal.AppendAsync(new(id, DateTimeOffset.Now, setting.Definition.Id, "RecoveryRequired", rollback.Message, before)); }
                throw;
            }
        }
        finally { gate.Release(); }
    }
    public async Task UndoAsync(IReversibleSetting setting, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var all = await journal.ReadAsync(token);
            var record = all.LastOrDefault(r => r.TaskId == setting.Definition.Id && r.Status == "Prepared" && !all.Any(x => x.Id == r.Id && x.Status == "Restored")) ?? throw new InvalidOperationException("No pending backup.");
            var current = setting.Read();
            if (current != setting.Target && current != record.Before) throw new InvalidOperationException("The setting changed externally. Review the backup before restoring.");
            setting.Write(record.Before!);
            if (setting.Read() != record.Before) throw new IOException("Restore verification failed.");
            await journal.AppendAsync(record with { Time = DateTimeOffset.Now, Status = "Restored", Detail = "Verified" });
        }
        finally { gate.Release(); }
    }
}

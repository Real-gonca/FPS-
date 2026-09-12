using Microsoft.Extensions.Logging;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application.Optimization;

/// <summary>
/// Pipeline canónico (spec §2.2): Backup → Apply → History → Rollback.
///
///  1. History: a ação é registada ANTES de aplicar (auditabilidade mesmo em falha);
///  2. Backup: todos os ChangeDescriptor são copiados para a base;
///  3. Apply: a tarefa efetua a alteração real;
///  4. Falha no apply → rollback AUTOMÁTICO dos backups criados;
///  5. Cada desfecho emite notificação tipada.
/// </summary>
public sealed class OptimizationOrchestrator
{
    private readonly IChangeApplier _applier;
    private readonly IHistoryStore _history;
    private readonly INotificationHub _notifications;
    private readonly ILogger<OptimizationOrchestrator> _log;

    public OptimizationOrchestrator(
        IChangeApplier applier,
        IHistoryStore history,
        INotificationHub notifications,
        ILogger<OptimizationOrchestrator> log)
    {
        _applier = applier;
        _history = history;
        _notifications = notifications;
        _log = log;
    }

    public async Task<OptimizationResult> ExecuteAsync(IOptimizationTask task, CancellationToken ct = default)
    {
        var action = new OptimizationAction
        {
            TaskKey = task.Key,
            Name = task.Name,
            Description = task.Description,
            Risk = task.Risk,
            Status = OptimizationStatus.Pending,
            StartedUtc = DateTimeOffset.UtcNow,
        };
        await _history.AddAsync(action, ct);
        _log.LogInformation("Tarefa {Key} iniciada (ação {ActionId}).", task.Key, action.Id);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // ── 1. BACKUP (antes de qualquer escrita) ─────────────────────────
            IReadOnlyList<Guid> backups;
            try
            {
                backups = await _applier.BackupAsync(task.DescribeChanges(), ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Backup da tarefa {Key} falhou — nada foi alterado.", task.Key);
                Finish(action, OptimizationStatus.Failed,
                    "O backup prévio falhou; a alteração NÃO foi aplicada. " + ex.Message, stopwatch);
                await _history.UpdateAsync(action, ct);
                await _notifications.EmitAsync(NotificationKind.Error, "Otimização abortada",
                    $"{task.Name}: não foi possível criar o backup. Nada foi alterado.");
                return OptimizationResult.Fail(action.ResultSummary, action.Id);
            }

            action.BackupIds.AddRange(backups);
            action.Status = OptimizationStatus.Running;
            await _history.UpdateAsync(action, ct);

            // ── 2. APPLY ──────────────────────────────────────────────────────
            try
            {
                await task.ApplyAsync(ct);
            }
            catch (Exception applyEx)
            {
                bool cancelled = applyEx is OperationCanceledException;
                _log.Log(cancelled ? LogLevel.Information : LogLevel.Error, applyEx,
                    "Tarefa {Key} falhou no apply{Suffix}.", task.Key, cancelled ? " (cancelada)" : string.Empty);

                OptimizationStatus status;
                string summary;
                try
                {
                    if (action.BackupIds.Count > 0)
                    {
                        await _applier.RestoreAsync(action.BackupIds, ct);
                        status = OptimizationStatus.RolledBack;
                        summary = cancelled
                            ? "Cancelada — o estado anterior foi restaurado."
                            : "Falhou — as alterações foram revertidas automaticamente. " + applyEx.Message;
                    }
                    else
                    {
                        status = OptimizationStatus.Failed;
                        summary = cancelled
                            ? "Cancelada antes de concluir."
                            : "Falhou: " + applyEx.Message;
                    }
                }
                catch (Exception rollbackEx)
                {
                    _log.LogError(rollbackEx, "Rollback automático da tarefa {Key} também falhou.", task.Key);
                    status = OptimizationStatus.Failed;
                    summary = "Falhou e o rollback automático não concluiu — verifique o histórico e faça rollback manual. " +
                              $"(erros: {applyEx.Message} / {rollbackEx.Message})";
                }

                Finish(action, status, summary, stopwatch);
                await _history.UpdateAsync(action, ct);
                await _notifications.EmitAsync(
                    status == OptimizationStatus.RolledBack ? NotificationKind.Warning : NotificationKind.Error,
                    "Otimização falhou",
                    $"{task.Name} — {summary}");

                if (cancelled)
                    throw;

                return status == OptimizationStatus.RolledBack
                    ? OptimizationResult.RolledBack(summary, action.Id)
                    : OptimizationResult.Fail(summary, action.Id);
            }

            // ── 3. SUCESSO ────────────────────────────────────────────────────
            Finish(action, OptimizationStatus.Success,
                task.Reversible
                    ? $"Concluído em {stopwatch.ElapsedMilliseconds} ms. Rollback disponível (backups: {backups.Count})."
                    : "Concluído. Esta ação não é reversível — consulte o histórico.",
                stopwatch);
            await _history.UpdateAsync(action, ct);
            await _notifications.EmitAsync(NotificationKind.Success, "Otimização aplicada",
                $"{task.Name} — {action.ResultSummary}");
            _log.LogInformation("Tarefa {Key} concluída com sucesso.", task.Key);
            return OptimizationResult.Ok(action.ResultSummary, action.Id);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private static void Finish(OptimizationAction action, OptimizationStatus status, string summary, Stopwatch stopwatch)
    {
        action.Status = status;
        action.ResultSummary = summary;
        action.FinishedUtc = DateTimeOffset.UtcNow;
        action.ElapsedMs = stopwatch.ElapsedMilliseconds;
    }
}

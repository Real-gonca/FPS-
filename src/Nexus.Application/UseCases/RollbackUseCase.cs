using Microsoft.Extensions.Logging;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application.UseCases;

/// <summary>
/// Caso de uso: rollback individual de uma ação do histórico (spec §4.10) —
/// restora os backups associados, em ordem inversa, e marca a ação.
/// </summary>
public sealed class RollbackUseCase
{
    private readonly IHistoryStore _history;
    private readonly IChangeApplier _applier;
    private readonly INotificationHub _notifications;
    private readonly ILogger<RollbackUseCase> _log;

    public RollbackUseCase(
        IHistoryStore history,
        IChangeApplier applier,
        INotificationHub notifications,
        ILogger<RollbackUseCase> log)
    {
        _history = history;
        _applier = applier;
        _notifications = notifications;
        _log = log;
    }

    public async Task<OptimizationResult> RollbackAsync(Guid actionId, CancellationToken ct = default)
    {
        var action = await _history.FindAsync(actionId, ct);
        if (action is null)
            return OptimizationResult.Fail("Ação não encontrada no histórico.");
        if (action.BackupIds.Count == 0)
            return OptimizationResult.Fail("Esta ação não tem backups associados (não é reversível).");

        _log.LogInformation("Rollback da ação {ActionId} ({Key}).", action.Id, action.TaskKey);

        try
        {
            await _applier.RestoreAsync(action.BackupIds, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Rollback da ação {ActionId} falhou.", action.Id);
            await _notifications.EmitAsync(NotificationKind.Error, "Rollback falhou",
                $"{action.Name}: {ex.Message}");
            return OptimizationResult.Fail("Rollback falhou: " + ex.Message, action.Id);
        }

        action.Status = OptimizationStatus.RolledBack;
        action.ResultSummary = "Revertida manualmente pelo utilizador.";
        action.FinishedUtc = DateTimeOffset.UtcNow;
        await _history.UpdateAsync(action, ct);

        await _notifications.EmitAsync(NotificationKind.Success, "Rollback concluído",
            $"{action.Name} — o estado anterior foi restaurado.");
        return OptimizationResult.Ok("O estado anterior foi restaurado.", action.Id);
    }
}

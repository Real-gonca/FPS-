using Microsoft.Extensions.Logging;
using Nexus.Application.Optimization;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application.UseCases;

/// <summary>
/// Caso de uso: aplicar uma otimização (comando da UI).
/// Faz o gatekeeping de elevação ANTES do pipeline (fallback elegante,
/// spec §2.2): tarefas que exigem admin ficam bloqueadas em modo limitado,
/// com notificação explicativa — nunca executam "de rastejo".
/// </summary>
public sealed class RunOptimizationUseCase
{
    private readonly IOptimizationTaskCatalog _catalog;
    private readonly IElevationService _elevation;
    private readonly OptimizationOrchestrator _orchestrator;
    private readonly INotificationHub _notifications;
    private readonly ILogger<RunOptimizationUseCase> _log;

    public RunOptimizationUseCase(
        IOptimizationTaskCatalog catalog,
        IElevationService elevation,
        OptimizationOrchestrator orchestrator,
        INotificationHub notifications,
        ILogger<RunOptimizationUseCase> log)
    {
        _catalog = catalog;
        _elevation = elevation;
        _orchestrator = orchestrator;
        _notifications = notifications;
        _log = log;
    }

    public async Task<OptimizationResult> ExecuteAsync(string taskKey, CancellationToken ct = default)
    {
        var task = _catalog.Find(taskKey);
        if (task is null)
        {
            _log.LogWarning("Tarefa {Key} não está registada nesta versão.", taskKey);
            return OptimizationResult.Fail(
                $"A tarefa '{taskKey}' não existe nesta versão da aplicação (planeada para um patch futuro).");
        }

        if (task.RequiresElevation && !_elevation.IsElevated)
        {
            await _notifications.EmitAsync(NotificationKind.Warning, "Requer administrador",
                $"{task.Name} altera configurações de sistema. Use 'Executar como Admin' no cabeçalho para continuar.");
            return OptimizationResult.Blocked("Requer privilégios de administrador (UAC).");
        }

        return await _orchestrator.ExecuteAsync(task, ct);
    }
}

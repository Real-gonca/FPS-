using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Acesso às tarefas agendadas com gatilho de logon/boot (Task Scheduler).
/// Implementado em Infrastructure via COM (<c>Schedule.Service</c>), evitando
/// dependência de parsing da saída localizada do <c>schtasks</c>.
/// </summary>
public interface IScheduledTaskService
{
    /// <summary>Lista as tarefas que disparam no logon ou no boot.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<ScheduledTaskInfo>> GetStartupTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>Habilita ou desabilita uma tarefa.</summary>
    /// <param name="taskPath">Caminho completo da tarefa (ex.: "\Microsoft\Windows\...\Task").</param>
    /// <param name="enabled">Novo estado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> SetEnabledAsync(string taskPath, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Executa uma tarefa imediatamente.</summary>
    /// <param name="taskPath">Caminho completo da tarefa.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> RunAsync(string taskPath, CancellationToken cancellationToken = default);
}

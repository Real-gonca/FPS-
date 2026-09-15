using System.Runtime.InteropServices;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Platform;

/// <summary>
/// Acesso ao Task Scheduler via COM (<c>Schedule.Service</c>).
/// </summary>
/// <remarks>
/// Usar a API COM (e não <c>schtasks.exe</c>) evita o parsing da saída localizada
/// do utilitário, que muda de formato entre idiomas e versões do Windows.
/// As tarefas internas de <c>\Microsoft\Windows\*</c> são ignoradas: o objetivo é
/// listar apenas itens que representam programas de terceiros iniciando no logon.
/// </remarks>
public sealed class ScheduledTaskService : IScheduledTaskService
{
    /// <summary>Prefixo das tarefas internas do Windows (ignoradas na listagem).</summary>
    private const string WindowsTaskPrefix = @"\Microsoft\Windows\";

    /// <summary>Profundidade máxima de recursão nas pastas de tarefas.</summary>
    private const int MaxFolderDepth = 4;

    /// <summary>TASK_TRIGGER_LOGON.</summary>
    private const int TriggerLogon = 7;

    /// <summary>TASK_TRIGGER_BOOT.</summary>
    private const int TriggerBoot = 8;

    private readonly ILogger<ScheduledTaskService> _logger;

    /// <summary>Cria o serviço de tarefas agendadas.</summary>
    /// <param name="logger">Logger.</param>
    public ScheduledTaskService(ILogger<ScheduledTaskService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduledTaskInfo>> GetStartupTasksAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<ScheduledTaskInfo>>(() =>
        {
            var result = new List<ScheduledTaskInfo>();

            if (!TryConnect(out var scheduler))
            {
                return result;
            }

            try
            {
                var root = scheduler!.GetFolder("\\");
                CollectFolder(root, result, 0, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enumerar as tarefas agendadas.");
            }
            finally
            {
                ReleaseCom(scheduler);
            }

            _logger.LogDebug("{Count} tarefa(s) de inicialização encontrada(s).", result.Count);

            return result;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> SetEnabledAsync(string taskPath, bool enabled, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!TryConnect(out var scheduler))
            {
                return false;
            }

            try
            {
                var task = scheduler!.GetTask(taskPath);

                if (task is null)
                {
                    _logger.LogWarning("Tarefa não encontrada: {TaskPath}.", taskPath);
                    return false;
                }

                task.Enabled = enabled;

                _logger.LogInformation("Tarefa '{Task}' {State}.", taskPath, enabled ? "habilitada" : "desabilitada");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao alterar o estado da tarefa '{Task}'.", taskPath);
                return false;
            }
            finally
            {
                ReleaseCom(scheduler);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RunAsync(string taskPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!TryConnect(out var scheduler))
            {
                return false;
            }

            try
            {
                scheduler!.GetTask(taskPath)?.Run(null);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao executar a tarefa '{Task}'.", taskPath);
                return false;
            }
            finally
            {
                ReleaseCom(scheduler);
            }
        }, cancellationToken);
    }

    /// <summary>Conecta ao serviço de agendamento de tarefas.</summary>
    private bool TryConnect(out dynamic? scheduler)
    {
        scheduler = null;

        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");

            if (type is null)
            {
                _logger.LogWarning("Componente Schedule.Service não está registrado neste sistema.");
                return false;
            }

            scheduler = Activator.CreateInstance(type);
            scheduler!.Connect();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível conectar ao Task Scheduler.");
            ReleaseCom(scheduler);
            scheduler = null;

            return false;
        }
    }

    /// <summary>Percorre uma pasta de tarefas recursivamente.</summary>
    private void CollectFolder(dynamic folder, List<ScheduledTaskInfo> result, int depth, CancellationToken cancellationToken)
    {
        if (depth > MaxFolderDepth)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        string folderPath;

        try
        {
            folderPath = (string)folder.Path;
        }
        catch (Exception)
        {
            return;
        }

        // Ignora tarefas internas do Windows.
        if (!folderPath.StartsWith(WindowsTaskPrefix, StringComparison.OrdinalIgnoreCase) && folderPath != "\\")
        {
            try
            {
                var tasks = folder.GetTasks(0);

                foreach (var task in tasks)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var info = TryReadTask(task);

                    if (info is not null)
                    {
                        result.Add(info);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Falha ao ler tarefas da pasta {Folder}.", folderPath);
            }
        }

        try
        {
            var subFolders = folder.GetFolders(0);

            foreach (var subFolder in subFolders)
            {
                CollectFolder(subFolder, result, depth + 1, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao enumerar subpastas de {Folder}.", folderPath);
        }
    }

    /// <summary>Lê uma tarefa e retorna suas informações (null quando não é de inicialização).</summary>
    private ScheduledTaskInfo? TryReadTask(dynamic task)
    {
        try
        {
            var definition = task.Definition;
            var triggerKind = ResolveTriggerKind(definition);

            if (triggerKind is null)
            {
                return null;
            }

            string? executable = null;
            var arguments = string.Empty;

            try
            {
                var actions = definition.Actions;

                if (actions.Count > 0)
                {
                    var action = actions[1];
                    executable = action.Path as string;
                    arguments = (action.Arguments as string) ?? string.Empty;
                }
            }
            catch (Exception)
            {
                // Tarefas sem ação Exec (ex.: envio de e-mail) não têm caminho.
            }

            string author;

            try
            {
                author = (definition.RegistrationInfo?.Author as string) ?? string.Empty;
            }
            catch (Exception)
            {
                author = string.Empty;
            }

            string runLevel;

            try
            {
                runLevel = (int)definition.Principal.RunLevel == 1 ? "Highest" : "Limited";
            }
            catch (Exception)
            {
                runLevel = "Limited";
            }

            return new ScheduledTaskInfo(
                Name: (string)task.Name,
                TaskPath: (string)task.Path,
                ExecutablePath: string.IsNullOrWhiteSpace(executable) ? null : executable,
                Arguments: arguments,
                Author: author,
                TriggerKind: triggerKind,
                IsEnabled: (bool)task.Enabled,
                RunLevel: runLevel);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao ler uma tarefa agendada.");
            return null;
        }
    }

    /// <summary>Identifica se a tarefa dispara no logon ou no boot.</summary>
    private static string? ResolveTriggerKind(dynamic definition)
    {
        try
        {
            var triggers = definition.Triggers;

            foreach (var trigger in triggers)
            {
                var type = (int)trigger.Type;

                if (type == TriggerLogon)
                {
                    return "Logon";
                }

                if (type == TriggerBoot)
                {
                    return "Boot";
                }
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    /// <summary>Libera um objeto COM.</summary>
    private static void ReleaseCom(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
        catch (Exception)
        {
            // Liberação de COM é melhor esforço.
        }
    }
}

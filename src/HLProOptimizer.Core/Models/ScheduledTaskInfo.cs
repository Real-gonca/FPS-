namespace HLProOptimizer.Core.Models;

/// <summary>Tarefa agendada com gatilho de inicialização.</summary>
/// <param name="Name">Nome da tarefa.</param>
/// <param name="TaskPath">Caminho completo (ex.: "\OneDrive Reporting Task").</param>
/// <param name="ExecutablePath">Executável disparado pela ação principal.</param>
/// <param name="Arguments">Argumentos da ação.</param>
/// <param name="Author">Autor da tarefa.</param>
/// <param name="TriggerKind">Tipo do gatilho ("Logon"/"Boot").</param>
/// <param name="IsEnabled">Se a tarefa está habilitada.</param>
/// <param name="RunLevel">Nível de execução (Limited/Highest).</param>
public sealed record ScheduledTaskInfo(
    string Name,
    string TaskPath,
    string? ExecutablePath,
    string Arguments,
    string Author,
    string TriggerKind,
    bool IsEnabled,
    string RunLevel);

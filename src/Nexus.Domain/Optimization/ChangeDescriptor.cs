namespace Nexus.Domain.Optimization;

/// <summary>
/// Descrição declarativa de uma alteração que a tarefa vai efetuar.
/// O orquestrador usa-a para criar o backup ANTES de aplicar (pipeline
/// Backup → Apply → History → Rollback, spec §2.2).
/// </summary>
/// <param name="Kind">Mecanismo (Registry/Service/File).</param>
/// <param name="Target">
/// Registry: caminho completo (ex.: "HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection").
/// Service: nome do serviço. File: caminho completo do ficheiro.
/// </param>
/// <param name="Detail">Registry: nome do valor. Vazio para os restantes.</param>
/// <param name="Description">Texto humano do que será guardado em backup.</param>
public sealed record ChangeDescriptor(ChangeKind Kind, string Target, string Detail, string Description);

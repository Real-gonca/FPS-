namespace Nexus.Domain.Ports;

/// <summary>Comando a executar (binário + argumentos literais — sem shell).</summary>
public sealed record CommandSpec(string FileName, string Arguments);

/// <summary>
/// Resultado de uma execução. <see cref="WasWhitelisted"/> = false indica que
/// o pedido foi REJEITADO pela whitelist (RejectionReason explica porquê).
/// </summary>
public sealed record CommandResult(
    bool Succeeded,
    bool WasWhitelisted,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string? RejectionReason)
{
    public static CommandResult Rejected(string reason) =>
        new(false, false, -1, string.Empty, string.Empty, reason);
}

/// <summary>
/// Executor de comandos de sistema com WHITELIST (spec §2.2 / §7):
/// "nenhum comando de sistema é executado sem estar previamente autorizado
/// numa lista explícita". Regras vivem no código (auditées com testes),
/// nunca em configuração em tempo de execução.
/// </summary>
public interface ISystemCommandExecutor
{
    Task<CommandResult> RunAsync(CommandSpec spec, CancellationToken ct = default);
}

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Commands;

/// <summary>
/// Executor de comandos com WHITELIST (spec §2.2): "nenhum comando de
/// sistema é executado sem estar previamente autorizado numa lista
/// explícita".
///
/// Ordem de verificação (todas obrigatórias):
///  1. binário pertence à whitelist;
///  2. argumentos não contêm metacaracteres de shell;
///  3. argumentos passam no padrão regex da regra;
/// depois executa via <see cref="IProcessRunner"/> (sem shell) com timeout.
///
/// Rejeições são LOGADAS e devolvidas como <see cref="CommandResult.Rejected"/>
/// — a aplicação nunca "silencia" um comando que alguém tentou rodar fora da
/// whitelist.
/// </summary>
public sealed class WhitelistedCommandExecutor : ISystemCommandExecutor
{
    private readonly IProcessRunner _runner;
    private readonly ILogger<WhitelistedCommandExecutor> _log;
    private readonly IReadOnlyList<CommandRule> _rules;

    public WhitelistedCommandExecutor(
        IProcessRunner runner,
        ILogger<WhitelistedCommandExecutor> log,
        IReadOnlyList<CommandRule>? rules = null)
    {
        _runner = runner;
        _log = log;
        _rules = rules ?? CommandWhitelist.Default;
    }

    public async Task<CommandResult> RunAsync(CommandSpec spec, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(spec.FileName))
            return Reject(spec, "Nome de comando vazio.");

        string fileName = Path.GetFileName(spec.FileName);

        var rule = _rules.FirstOrDefault(r =>
            string.Equals(Path.GetFileName(r.FileName), fileName, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
            return Reject(spec,
                $"'{fileName}' não está na whitelist do CommandExecutor — nenhum comando roda sem autorização explícita.");

        if (CommandWhitelist.ContainsShellMetacharacters(spec.Arguments))
            return Reject(spec,
                "Os argumentos contêm metacaracteres de shell (& | < > ^ ; parênteses) — bloqueados.");

        if (!Regex.IsMatch(spec.Arguments, rule.ArgumentsPattern, RegexOptions.Compiled))
            return Reject(spec,
                $"Os argumentos '{spec.Arguments}' não passam no padrão permitido para '{fileName}' ({rule.Justification}).");

        try
        {
            _log.LogInformation("Comando autorizado: {File} {Args} — {Why}", fileName, spec.Arguments, rule.Justification);
            var result = await _runner.RunAsync(rule.FileName, spec.Arguments, TimeSpan.FromSeconds(rule.TimeoutSeconds), ct);
            return new CommandResult(
                result.ExitCode == 0,
                WasWhitelisted: true,
                result.ExitCode,
                result.StandardOutput,
                result.StandardError,
                RejectionReason: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Comando autorizado {File} falhou na execução.", fileName);
            return new CommandResult(false, true, -1, string.Empty, ex.Message, null);
        }
    }

    private CommandResult Reject(CommandSpec spec, string reason)
    {
        _log.LogWarning("Comando REJEITADO: {Spec} — {Reason}", spec, reason);
        return CommandResult.Rejected(reason);
    }
}

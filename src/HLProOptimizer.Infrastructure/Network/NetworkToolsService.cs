using System.Globalization;
using System.Net.NetworkInformation;
using System.Text;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Network;

/// <summary>
/// Ferramentas de reparo e ajuste de rede.
/// </summary>
/// <remarks>
/// <para>
/// <b>Um único prompt de UAC por operação.</b> Reset de Winsock/TCP-IP, release e
/// renew exigem elevação. Executar cada comando separadamente geraria quatro
/// prompts; por isso montamos um <c>.cmd</c> temporário com marcadores
/// (<c>[STEP]</c>/<c>[EXIT]</c>), elevamos UMA vez e depois fatiamos o log de
/// saída em <see cref="CommandResult"/> por etapa — o usuário vê o progresso real
/// de cada passo sem ser bombardeado pelo UAC.
/// </para>
/// <para>
/// <b>Ajustes de baixa latência</b> desativam o algoritmo de Nagle por interface
/// (<c>TcpAckFrequency=1</c>, <c>TCPNoDelay=1</c>), removem o throttling de rede
/// multimídia e zeram a responsividade do sistema (valores clássicos para jogos
/// competitivos). MTU e Energy-Efficient Ethernet são deliberadamente ignorados:
/// dependem do driver do fabricante e um valor errado derruba a conexão.
/// </para>
/// </remarks>
public sealed class NetworkToolsService : INetworkToolsService
{
    private const string TcpipInterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string SystemProfileKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    /// <summary>Valor padrão do Windows: limita a rede a 10 pacotes/ms durante multimídia.</summary>
    private const int DefaultNetworkThrottlingIndex = 10;

    /// <summary>Valor padrão do Windows: reserva 20% do tempo de CPU para tarefas do sistema.</summary>
    private const int DefaultSystemResponsiveness = 20;

    private readonly ICommandRunner _commands;
    private readonly IRegistryService _registry;
    private readonly ISystemPaths _paths;
    private readonly ILogger<NetworkToolsService> _logger;

    /// <summary>Cria o serviço de ferramentas de rede.</summary>
    /// <param name="commands">Executor de comandos.</param>
    /// <param name="registry">Registro do Windows.</param>
    /// <param name="paths">Caminhos do aplicativo (temporários).</param>
    /// <param name="logger">Logger.</param>
    public NetworkToolsService(
        ICommandRunner commands,
        IRegistryService registry,
        ISystemPaths paths,
        ILogger<NetworkToolsService> logger)
    {
        _commands = commands;
        _registry = registry;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<CommandResult> ResetTcpIpAsync(CancellationToken cancellationToken = default)
        => RunSingleAsync("netsh.exe", "int ip reset", requireElevation: true, timeout: TimeSpan.FromMinutes(2), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> ResetWinsockAsync(CancellationToken cancellationToken = default)
        => RunSingleAsync("netsh.exe", "winsock reset", requireElevation: true, timeout: TimeSpan.FromMinutes(2), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> ReleaseIpAsync(CancellationToken cancellationToken = default)
        => RunSingleAsync("ipconfig.exe", "/release", requireElevation: true, timeout: TimeSpan.FromMinutes(1), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> RenewIpAsync(CancellationToken cancellationToken = default)
        => RunSingleAsync("ipconfig.exe", "/renew", requireElevation: true, timeout: TimeSpan.FromMinutes(2), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommandResult>> RepairNetworkAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var steps = new (string Id, string Title, string Command)[]
        {
            ("flushdns", "Limpando o cache DNS", "ipconfig /flushdns"),
            ("release", "Liberando o endereço IP", "ipconfig /release"),
            ("renew", "Renovando o endereço IP", "ipconfig /renew"),
            ("winsock", "Reiniciando o catálogo Winsock", "netsh winsock reset"),
            ("tcpip", "Reiniciando a pilha TCP/IP", $"netsh int ip reset \"{Path.Combine(_paths.TempDirectory, "hl-ip-reset.log")}\""),
            ("registerdns", "Registrando o DNS", "ipconfig /registerdns")
        };

        progress?.Report("Preparando o reparo de rede...");

        var logFile = Path.Combine(_paths.TempDirectory, $"hl-net-repair-{Guid.NewGuid():N}.log");
        var scriptFile = Path.Combine(_paths.TempDirectory, $"hl-net-repair-{Guid.NewGuid():N}.cmd");

        try
        {
            File.WriteAllText(scriptFile, BuildRepairScript(steps, logFile), Encoding.UTF8);

            _logger.LogInformation("Executando o reparo completo de rede ({Steps} etapas).", steps.Length);

            progress?.Report("Solicitando permissões de administrador...");

            var elevated = !ElevationHelper.IsProcessElevated();

            var result = await _commands
                .RunAsync("cmd.exe", $"/c \"{scriptFile}\"", cancellationToken, elevated: elevated, timeout: TimeSpan.FromMinutes(10))
                .ConfigureAwait(false);

            if (result.ExitCode == 1223)
            {
                progress?.Report("Elevação cancelada pelo usuário. O reparo não foi executado.");
                _logger.LogWarning("Reparo de rede cancelado (UAC recusado).");

                return [new CommandResult("cmd.exe", scriptFile, 1223, string.Empty, "Elevação recusada pelo usuário.", TimeSpan.Zero, true)];
            }

            var stepResults = ParseScriptLog(logFile, steps);

            foreach (var stepResult in stepResults)
            {
                progress?.Report(stepResult.IsSuccess
                    ? $"✔ {stepResult.FileName}: concluído."
                    : $"⚠ {stepResult.FileName}: exit code {stepResult.ExitCode}.");
            }

            progress?.Report("Reparo de rede finalizado. Pode ser necessário reiniciar o computador.");

            _logger.LogInformation(
                "Reparo de rede concluído: {Ok}/{Total} etapas com sucesso.",
                stepResults.Count(r => r.IsSuccess),
                stepResults.Count);

            return stepResults;
        }
        catch (OperationCanceledException)
        {
            progress?.Report("Reparo de rede cancelado.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha inesperada durante o reparo de rede.");
            progress?.Report($"Erro durante o reparo: {ex.Message}");

            return [new CommandResult("cmd.exe", scriptFile, -1, string.Empty, ex.Message, TimeSpan.Zero, true)];
        }
        finally
        {
            TryDelete(scriptFile);
            TryDelete(logFile);
        }
    }

    /// <inheritdoc />
    public async Task<double?> MeasureLatencyAsync(
        string host,
        int timeoutMs = 1000,
        int attempts = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var normalizedAttempts = Math.Clamp(attempts, 1, 16);
        var normalizedTimeout = Math.Clamp(timeoutMs, 100, 10_000);
        var roundTrips = new List<double>();

        for (var attempt = 1; attempt <= normalizedAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, normalizedTimeout).ConfigureAwait(false);

                if (reply.Status == IPStatus.Success)
                {
                    roundTrips.Add(reply.RoundtripTime);
                }
                else
                {
                    _logger.LogDebug("Ping {Attempt}/{Total} para {Host}: {Status}.", attempt, normalizedAttempts, host, reply.Status);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Host inexistente / ICMP bloqueado pelo firewall.
                _logger.LogDebug(ex, "Ping {Attempt}/{Total} para {Host} falhou.", attempt, normalizedAttempts, host);
            }

            if (attempt < normalizedAttempts)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }

        if (roundTrips.Count == 0)
        {
            _logger.LogWarning("Nenhuma resposta de {Host} em {Attempts} tentativa(s).", host, normalizedAttempts);
            return null;
        }

        var average = Math.Round(roundTrips.Average(), 1);

        _logger.LogInformation("Latência média para {Host}: {Average} ms ({Ok}/{Total} respostas).", host, average, roundTrips.Count, normalizedAttempts);

        return average;
    }

    /// <inheritdoc />
    public Task<bool> ApplyLowLatencyTweaksAsync(CancellationToken cancellationToken = default)
    {
        var regArguments = new List<string>();

        // 1) Nagle desativado por interface (menos atraso em pacotes pequenos = jogos).
        foreach (var interfaceId in GetInterfaceIds())
        {
            var key = $@"HKLM\{TcpipInterfacesKey}\{interfaceId}";

            regArguments.Add($"add \"{key}\" /v TcpAckFrequency /t REG_DWORD /d 1 /f");
            regArguments.Add($"add \"{key}\" /v TCPNoDelay /t REG_DWORD /d 1 /f");
        }

        // 2) Throttling de rede multimídia desativado e CPU dedicada ao aplicativo.
        regArguments.Add($"add \"HKLM\\{SystemProfileKey}\" /v NetworkThrottlingIndex /t REG_DWORD /d 4294967295 /f");
        regArguments.Add($"add \"HKLM\\{SystemProfileKey}\" /v SystemResponsiveness /t REG_DWORD /d 0 /f");

        _logger.LogInformation("Aplicando ajustes de baixa latência ({Count} valores de registro).", regArguments.Count);

        return RunRegistryBatchAsync(regArguments, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RevertLowLatencyTweaksAsync(CancellationToken cancellationToken = default)
    {
        var regArguments = new List<string>();

        // 1) Remove os valores por interface (o Windows volta ao comportamento padrão).
        foreach (var interfaceId in GetInterfaceIds())
        {
            var key = $@"HKLM\{TcpipInterfacesKey}\{interfaceId}";

            regArguments.Add($"delete \"{key}\" /v TcpAckFrequency /f");
            regArguments.Add($"delete \"{key}\" /v TCPNoDelay /f");
        }

        // 2) Restaura os padrões do Windows.
        regArguments.Add($"add \"HKLM\\{SystemProfileKey}\" /v NetworkThrottlingIndex /t REG_DWORD /d {DefaultNetworkThrottlingIndex} /f");
        regArguments.Add($"add \"HKLM\\{SystemProfileKey}\" /v SystemResponsiveness /t REG_DWORD /d {DefaultSystemResponsiveness} /f");

        _logger.LogInformation("Revertendo ajustes de baixa latência ({Count} valores de registro).", regArguments.Count);

        return RunRegistryBatchAsync(regArguments, cancellationToken);
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    /// <summary>Executa um único comando (elevando apenas quando necessário).</summary>
    private async Task<CommandResult> RunSingleAsync(
        string fileName,
        string arguments,
        bool requireElevation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var elevated = requireElevation && !ElevationHelper.IsProcessElevated();

        _logger.LogInformation("Executando {FileName} {Arguments} (elevado={Elevated}).", fileName, arguments, elevated);

        try
        {
            var result = await _commands
                .RunAsync(fileName, arguments, cancellationToken, elevated: elevated, timeout: timeout)
                .ConfigureAwait(false);

            if (result.ExitCode == 1223)
            {
                _logger.LogWarning("{FileName} {Arguments} não executado: elevação recusada.", fileName, arguments);
            }
            else if (!result.IsSuccess)
            {
                _logger.LogWarning("{FileName} {Arguments} falhou (exit {ExitCode}): {Output}", fileName, arguments, result.ExitCode, result.CombinedOutput);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao executar {FileName} {Arguments}.", fileName, arguments);

            return new CommandResult(fileName, arguments, -1, string.Empty, ex.Message, TimeSpan.Zero, elevated);
        }
    }

    /// <summary>
    /// Executa um lote de argumentos de <c>reg.exe</c> com, no máximo, uma elevação.
    /// </summary>
    /// <param name="regArguments">Argumentos sem o executável (ex.: <c>add "HKLM\..." /v X /t REG_DWORD /d 1 /f</c>).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    private async Task<bool> RunRegistryBatchAsync(IReadOnlyList<string> regArguments, CancellationToken cancellationToken)
    {
        if (regArguments.Count == 0)
        {
            return true;
        }

        // Já elevados: executa direto, um comando por vez (sem prompts adicionais).
        if (ElevationHelper.IsProcessElevated())
        {
            var failures = 0;

            foreach (var arguments in regArguments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var result = await _commands
                        .RunAsync("reg.exe", arguments, cancellationToken, timeout: TimeSpan.FromSeconds(30))
                        .ConfigureAwait(false);

                    if (!result.IsSuccess)
                    {
                        failures++;
                        _logger.LogWarning("reg.exe {Arguments} falhou (exit {ExitCode}).", arguments, result.ExitCode);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures++;
                    _logger.LogWarning(ex, "Falha no comando de registro: reg.exe {Arguments}.", arguments);
                }
            }

            return failures == 0;
        }

        // Não elevados: monta um .cmd e pede UAC uma única vez para todo o lote.
        var scriptFile = Path.Combine(_paths.TempDirectory, $"hl-net-tweaks-{Guid.NewGuid():N}.cmd");

        try
        {
            var script = new StringBuilder();
            script.AppendLine("@echo off");

            foreach (var arguments in regArguments)
            {
                // > nul 2>&1: reg.exe é verboso e a saída não é necessária.
                script.AppendLine($"reg {arguments} > nul 2>&1");
            }

            script.AppendLine("exit /b 0");

            File.WriteAllText(scriptFile, script.ToString(), Encoding.UTF8);

            var result = await _commands
                .RunAsync("cmd.exe", $"/c \"{scriptFile}\"", cancellationToken, elevated: true, timeout: TimeSpan.FromMinutes(3))
                .ConfigureAwait(false);

            if (result.ExitCode == 1223)
            {
                _logger.LogWarning("Ajustes de rede não aplicados: elevação recusada pelo usuário.");
                return false;
            }

            _logger.LogInformation("Lote de ajustes de rede executado (exit {ExitCode}).", result.ExitCode);

            return result.IsSuccess;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao executar o lote de ajustes de rede.");
            return false;
        }
        finally
        {
            TryDelete(scriptFile);
        }
    }

    /// <summary>GUIDs das interfaces TCP/IP ativas.</summary>
    private IReadOnlyList<string> GetInterfaceIds()
    {
        try
        {
            // 1) Chaves existentes no registro (fonte da verdade do Tcpip).
            var registryIds = _registry.GetSubKeyNames(RegistryHiveKind.LocalMachine, TcpipInterfacesKey);

            if (registryIds.Count > 0)
            {
                return registryIds;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao enumerar as interfaces TCP/IP no registro.");
        }

        try
        {
            // 2) Fallback: adaptadores de rede ativos do sistema.
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .Where(ni => ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(ni => ni.Id)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao enumerar os adaptadores de rede.");
            return [];
        }
    }

    /// <summary>Monta o script de reparo com marcadores de etapa.</summary>
    private static string BuildRepairScript(IReadOnlyList<(string Id, string Title, string Command)> steps, string logFile)
    {
        var script = new StringBuilder();

        script.AppendLine("@echo off");

        // Saída em UTF-8 para a leitura do log ser determinística.
        script.AppendLine("chcp 65001 > nul");
        script.AppendLine($"if exist \"{logFile}\" del /q \"{logFile}\"");

        foreach (var (id, _, command) in steps)
        {
            script.AppendLine($"echo [STEP] {id}>> \"{logFile}\"");
            script.AppendLine($"{command}>> \"{logFile}\" 2>&1");
            script.AppendLine($"echo [EXIT] %ERRORLEVEL%>> \"{logFile}\"");
        }

        script.AppendLine("exit /b 0");

        return script.ToString();
    }

    /// <summary>Converte o log do script em um <see cref="CommandResult"/> por etapa.</summary>
    private static IReadOnlyList<CommandResult> ParseScriptLog(
        string logFile,
        IReadOnlyList<(string Id, string Title, string Command)> steps)
    {
        var byId = new Dictionary<string, (int ExitCode, StringBuilder Output)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!File.Exists(logFile))
            {
                return steps
                    .Select(s => new CommandResult(s.Title, s.Command, -1, string.Empty, "Log de saída não encontrado.", TimeSpan.Zero, true))
                    .ToList();
            }

            string currentId = string.Empty;

            foreach (var rawLine in File.ReadAllLines(logFile, Encoding.UTF8))
            {
                var line = rawLine.TrimEnd();

                if (line.StartsWith("[STEP] ", StringComparison.OrdinalIgnoreCase))
                {
                    currentId = line["[STEP] ".Length..].Trim();
                    byId[currentId] = (0, new StringBuilder());
                    continue;
                }

                if (line.StartsWith("[EXIT] ", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentId.Length > 0 && byId.TryGetValue(currentId, out var entry))
                    {
                        var exitCode = int.TryParse(line["[EXIT] ".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                            ? parsed
                            : -1;

                        byId[currentId] = (exitCode, entry.Output);
                    }

                    currentId = string.Empty;
                    continue;
                }

                if (currentId.Length > 0 && byId.TryGetValue(currentId, out var current))
                {
                    current.Output.AppendLine(line);
                }
            }
        }
        catch (Exception)
        {
            // Log ilegível: as etapas são reportadas como desconhecidas abaixo.
        }

        return steps
            .Select(step => byId.TryGetValue(step.Id, out var entry)
                ? new CommandResult(step.Title, step.Command, entry.ExitCode, entry.Output.ToString().TrimEnd(), string.Empty, TimeSpan.Zero, true)
                : new CommandResult(step.Title, step.Command, -1, string.Empty, "Etapa não executada.", TimeSpan.Zero, true))
            .ToList();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Arquivo temporário: falhar na limpeza não é crítico.
        }
    }
}

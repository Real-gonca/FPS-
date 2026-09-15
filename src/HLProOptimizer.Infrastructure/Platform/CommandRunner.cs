using System.Diagnostics;
using System.Text;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Exceptions;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Platform;

/// <summary>
/// Execução de ferramentas nativas do Windows (powercfg, netsh, ipconfig, sfc,
/// dism, reg, pnputil, schtasks, MpCmdRun).
/// </summary>
/// <remarks>
/// <para>
/// <b>Detecção de encoding:</b> utilitários como <c>sfc</c> e <c>powercfg</c>
/// emitem saída UTF-16LE com BOM quando a saída é redirecionada. O
/// <see cref="StreamReader"/> é criado com <c>detectEncodingFromByteOrderMarks</c>
/// para decodificar corretamente tanto UTF-8 quanto UTF-16 - sem isso, o texto
/// chega cheio de caracteres nulos e nenhum parser funciona.
/// </para>
/// <para>
/// <b>Timeout e cancelamento:</b> processos travados são encerrados
/// (incluindo a árvore) para nunca bloquear a UI.
/// </para>
/// </remarks>
public sealed class CommandRunner : ICommandRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger<CommandRunner> _logger;

    /// <summary>Cria o executor de comandos.</summary>
    /// <param name="logger">Logger.</param>
    public CommandRunner(ILogger<CommandRunner> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default,
        bool elevated = false,
        TimeSpan? timeout = null,
        string? workingDirectory = null,
        Action<string>? onOutputLine = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("O executável não pode ser vazio.", nameof(fileName));
        }

        var stopwatch = Stopwatch.StartNew();
        var output = new StringBuilder();
        var error = new StringBuilder();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            CreateNoWindow = true,
            UseShellExecute = elevated,
            RedirectStandardOutput = !elevated,
            RedirectStandardError = !elevated,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.System)
                : workingDirectory
        };

        if (elevated)
        {
            startInfo.Verb = "runas";
        }

        _logger.LogDebug("Executando: {FileName} {Arguments} (elevado={Elevated}).", fileName, arguments, elevated);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                return new CommandResult(fileName, arguments, -1, string.Empty, "Falha ao iniciar o processo.", stopwatch.Elapsed, elevated);
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // 1223 = ERROR_CANCELLED (usuário recusou o prompt de UAC).
            if (ex.NativeErrorCode == 1223)
            {
                _logger.LogInformation("Elevação recusada pelo usuário para '{FileName}'.", fileName);
                return new CommandResult(fileName, arguments, 1223, string.Empty, "Elevação recusada pelo usuário.", stopwatch.Elapsed, elevated);
            }

            _logger.LogError(ex, "Não foi possível iniciar '{FileName}'.", fileName);
            throw new OptimizerException($"Não foi possível executar '{fileName}'.", ex);
        }

        if (!elevated)
        {
            await CaptureOutputAsync(process, output, error, onOutputLine, cancellationToken).ConfigureAwait(false);
        }

        var effectiveTimeout = timeout ?? DefaultTimeout;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout de {Timeout} ao executar '{FileName} {Arguments}'.", effectiveTimeout, fileName, arguments);
            TryKillTree(process);

            stopwatch.Stop();

            return new CommandResult(
                fileName,
                arguments,
                -2,
                output.ToString(),
                $"Tempo limite de {effectiveTimeout.TotalSeconds:F0}s excedido.",
                stopwatch.Elapsed,
                elevated);
        }

        stopwatch.Stop();

        var result = new CommandResult(
            fileName,
            arguments,
            process.ExitCode,
            output.ToString(),
            error.ToString(),
            stopwatch.Elapsed,
            elevated);

        _logger.LogDebug(
            "Comando '{FileName}' finalizado em {Elapsed}ms com código {ExitCode}.",
            fileName, stopwatch.ElapsedMilliseconds, result.ExitCode);

        return result;
    }

    /// <inheritdoc />
    public async Task<int> RunHiddenAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Falha ao executar o comando oculto '{FileName}'.", fileName);
            return -1;
        }
    }

    /// <summary>Lê stdout/stderr linha a linha, reportando em tempo real.</summary>
    private async Task CaptureOutputAsync(
        Process process,
        StringBuilder output,
        StringBuilder error,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        var stdoutTask = ReadStreamAsync(process.StandardOutput.BaseStream, output, onOutputLine, cancellationToken);
        var stderrTask = ReadStreamAsync(process.StandardError.BaseStream, error, null, cancellationToken);

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
    }

    /// <summary>Lê uma stream até o fim, acumulando e reportando linhas.</summary>
    private static async Task ReadStreamAsync(
        Stream stream,
        StringBuilder accumulator,
        Action<string>? onLine,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            accumulator.AppendLine(line);
            onLine?.Invoke(line);
        }
    }

    /// <summary>Encerra o processo e seus filhos.</summary>
    private void TryKillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível encerrar o processo {Pid}.", process.Id);
        }
    }
}

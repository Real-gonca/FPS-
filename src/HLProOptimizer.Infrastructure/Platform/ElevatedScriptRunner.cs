using System.Text;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Platform;

/// <summary>
/// Executa ferramentas longas do Windows (<c>sfc</c>, <c>dism</c>, <c>chkdsk</c>,
/// <c>pnputil</c>) reportando a saída em tempo real, mesmo quando é preciso elevar.
/// </summary>
/// <remarks>
/// <para>
/// <b>O problema.</b> Processos elevados via <c>ShellExecute</c> (Verb=runas) não
/// permitem redirecionar stdout — logo não dá para fazer streaming da saída.
/// </para>
/// <para>
/// <b>A solução.</b> Quando o processo já é administrador, executamos direto com
/// redirecionamento. Quando não é, gravamos um <c>.cmd</c> temporário que redireciona
/// a saída para um arquivo de log e pedimos elevação UMA vez; em paralelo, um laço
/// faz <i>tail</i> do log e publica as linhas novas no <see cref="IProgress{T}"/>.
/// O usuário vê "Verificando 23%..." exatamente como num console elevado.
/// </para>
/// <para>
/// O log é lido com detecção de BOM porque <c>sfc.exe</c> grava em UTF-16LE e o
/// <c>dism.exe</c> em UTF-8 (com <c>chcp 65001</c>).
/// </para>
/// </remarks>
public sealed class ElevatedScriptRunner
{
    /// <summary>Intervalo entre leituras do log durante o tail.</summary>
    private static readonly TimeSpan TailInterval = TimeSpan.FromMilliseconds(400);

    private readonly ICommandRunner _commands;
    private readonly ISystemPaths _paths;
    private readonly ILogger<ElevatedScriptRunner> _logger;

    /// <summary>Cria o executor.</summary>
    /// <param name="commands">Executor de comandos.</param>
    /// <param name="paths">Caminhos do aplicativo (arquivos temporários).</param>
    /// <param name="logger">Logger.</param>
    public ElevatedScriptRunner(ICommandRunner commands, ISystemPaths paths, ILogger<ElevatedScriptRunner> logger)
    {
        _commands = commands;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Executa uma ferramenta com streaming de saída e elevação sob demanda.</summary>
    /// <param name="fileName">Executável (ex.: "sfc.exe").</param>
    /// <param name="arguments">Argumentos (ex.: "/scannow").</param>
    /// <param name="progress">Recebe cada linha de saída.</param>
    /// <param name="timeout">Tempo máximo.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado com a saída completa no <see cref="CommandResult.StandardOutput"/>.</returns>
    public Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        IProgress<string>? progress = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(30);

        return ElevationHelper.IsProcessElevated()
            ? RunDirectAsync(fileName, arguments, progress, effectiveTimeout, cancellationToken)
            : RunElevatedWithLogAsync(fileName, arguments, progress, effectiveTimeout, cancellationToken);
    }

    /// <summary>Execução direta (processo já é administrador): saída redirecionada e streaming imediato.</summary>
    private async Task<CommandResult> RunDirectAsync(
        string fileName,
        string arguments,
        IProgress<string>? progress,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executando (já elevado): {FileName} {Arguments}.", fileName, arguments);

        return await _commands
            .RunAsync(
                fileName,
                arguments,
                cancellationToken,
                elevated: false,
                timeout: timeout,
                onOutputLine: line => progress?.Report(line))
            .ConfigureAwait(false);
    }

    /// <summary>Execução elevada com log em arquivo + tail para streaming.</summary>
    private async Task<CommandResult> RunElevatedWithLogAsync(
        string fileName,
        string arguments,
        IProgress<string>? progress,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");
        var logFile = Path.Combine(_paths.TempDirectory, $"hl-elevated-{token}.log");
        var scriptFile = Path.Combine(_paths.TempDirectory, $"hl-elevated-{token}.cmd");

        _logger.LogInformation("Executando (elevação necessária): {FileName} {Arguments}.", fileName, arguments);

        try
        {
            File.WriteAllText(
                scriptFile,
                BuildScript(fileName, arguments, logFile),
                Encoding.UTF8);

            var runTask = _commands.RunAsync("cmd.exe", $"/c \"{scriptFile}\"", cancellationToken, elevated: true, timeout: timeout);
            var tailTask = TailLogAsync(logFile, progress, runTask, cancellationToken);

            var result = await runTask.ConfigureAwait(false);

            try
            {
                await tailTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Tail encerrado por timeout do processo: a saída final ainda é lida abaixo.
            }

            var output = ReadRemaining(logFile, out _);

            if (result.ExitCode == 1223)
            {
                progress?.Report("Elevação recusada pelo usuário.");
                _logger.LogWarning("{FileName} não executado: elevação recusada.", fileName);

                return new CommandResult(fileName, arguments, 1223, string.Empty, "Elevação recusada pelo usuário.", result.Duration, true);
            }

            return result with
            {
                FileName = fileName,
                Arguments = arguments,
                StandardOutput = output,
                WasElevated = true
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao executar {FileName} {Arguments} de forma elevada.", fileName, arguments);

            return new CommandResult(fileName, arguments, -1, string.Empty, ex.Message, TimeSpan.Zero, true);
        }
        finally
        {
            TryDelete(scriptFile);
            TryDelete(logFile);
        }
    }

    /// <summary>Monta o script que redireciona a saída da ferramenta para o log.</summary>
    private static string BuildScript(string fileName, string arguments, string logFile)
    {
        var script = new StringBuilder();

        script.AppendLine("@echo off");

        // Code page UTF-8: mantém acentos da saída do DISM legíveis no log.
        script.AppendLine("chcp 65001 > nul");
        script.AppendLine($"if exist \"{logFile}\" del /q \"{logFile}\"");

        // Caminho completo quando possível (System32 é o working directory padrão).
        var executable = fileName.Contains('\\') || fileName.Contains('/')
            ? fileName
            : Path.Combine(Environment.SystemDirectory, fileName);

        script.AppendLine($"\"{executable}\" {arguments}>> \"{logFile}\" 2>&1");
        script.AppendLine("exit /b %ERRORLEVEL%");

        return script.ToString();
    }

    /// <summary>Publica as linhas novas do log enquanto o processo elevado roda.</summary>
    private async Task TailLogAsync(
        string logFile,
        IProgress<string>? progress,
        Task<CommandResult> runTask,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return;
        }

        var offset = 0L;

        while (!runTask.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            offset = ReadNewLines(logFile, offset, progress);

            await Task.Delay(TailInterval, cancellationToken).ConfigureAwait(false);
        }

        // Drenagem final: o processo pode ter escrito entre a última leitura e o término.
        ReadNewLines(logFile, offset, progress);
    }

    /// <summary>Lê as linhas adicionadas desde <paramref name="offset"/> e retorna o novo offset.</summary>
    private long ReadNewLines(string logFile, long offset, IProgress<string> progress)
    {
        var (text, newOffset) = ReadFromOffset(logFile, offset);

        if (text.Length == 0)
        {
            return offset;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Length > 0)
            {
                progress.Report(line);
            }
        }

        return newOffset;
    }

    /// <summary>Lê o conteúdo restante do log (usado para montar o resultado final).</summary>
    private static string ReadRemaining(string logFile, out long offset)
    {
        var (text, newOffset) = ReadFromOffset(logFile, 0);

        offset = newOffset;

        return text;
    }

    /// <summary>
    /// Lê bytes novos do arquivo a partir de um offset, tolerando o arquivo ainda
    /// não existir e o compartilhamento de escrita do cmd.exe.
    /// </summary>
    private static (string Text, long NewOffset) ReadFromOffset(string logFile, long offset)
    {
        try
        {
            if (!File.Exists(logFile))
            {
                return (string.Empty, offset);
            }

            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (stream.Length <= offset)
            {
                return (string.Empty, offset);
            }

            stream.Seek(offset, SeekOrigin.Begin);

            // BOM detection cuida do UTF-16LE do sfc.exe e do UTF-8 do dism.exe.
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = reader.ReadToEnd();

            return (text, stream.Position);
        }
        catch (IOException)
        {
            // Arquivo momentaneamente bloqueado: tenta de novo no próximo tick.
            return (string.Empty, offset);
        }
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
            // Arquivos temporários: limpeza é melhor esforço.
        }
    }
}

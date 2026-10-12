using System.Diagnostics;
using System.Text;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services.System;

/// <summary>
/// Executa comandos externos sem abrir janelas, com timeout e captura de saída.
/// Todas as invocações ficam registadas (comando, código de saída e duração) para auditoria.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;
    private readonly TimeSpan _timeout;

    public ProcessRunner(ILogger<ProcessRunner> logger, TimeSpan? timeout = null)
    {
        _logger = logger;
        _timeout = timeout ?? TimeSpan.FromMilliseconds(Helpers.AppConstants.CommandTimeoutMs);
    }

    public async Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        var psi = BuildStartInfo(fileName, arguments);
        var sw = Stopwatch.StartNew();

        try
        {
            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            if (!process.Start())
            {
                _logger.LogWarning("Não foi possível iniciar {File} {Args}", fileName, arguments);
                return new CommandResult(-1, string.Empty, "Processo não iniciado.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                _logger.LogWarning("Timeout ({Timeout} ms) ao executar {File} {Args}", _timeout.TotalMilliseconds, fileName, arguments);
                return new CommandResult(-2, stdout.ToString(), $"Tempo limite excedido ({_timeout.TotalSeconds:N0} s).");
            }

            var result = new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString());
            _logger.LogDebug("{File} {Args} → exit {Code} em {Ms} ms", fileName, arguments, result.ExitCode, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao executar {File} {Args}", fileName, arguments);
            return new CommandResult(-3, string.Empty, ex.Message);
        }
    }

    public CommandResult Run(string fileName, string arguments) => RunAsync(fileName, arguments).GetAwaiter().GetResult();

    public bool FileExists(string fileName)
    {
        try
        {
            if (Path.IsPathRooted(fileName)) return File.Exists(fileName);

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var ext in new[] { ".exe", ".cmd", ".bat", string.Empty })
                {
                    var candidate = Path.Combine(dir.Trim(), fileName + ext);
                    if (File.Exists(candidate)) return true;
                }
            }

            // Ferramentas do sistema ficam em System32 e nem sempre estão no PATH do processo elevado.
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return File.Exists(Path.Combine(system32, fileName + ".exe"));
        }
        catch
        {
            return false;
        }
    }

    private static ProcessStartInfo BuildStartInfo(string fileName, string arguments) => new()
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        WindowStyle = ProcessWindowStyle.Hidden,
    };

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // O processo pode ter terminado entre a verificação e o Kill — ignorar.
        }
    }
}

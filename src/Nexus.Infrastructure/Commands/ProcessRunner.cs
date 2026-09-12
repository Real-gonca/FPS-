using System.Diagnostics;

namespace Nexus.Infrastructure.Commands;

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

/// <summary>
/// Abstração da execução de processos — permite testar o executor de
/// whitelist SEM executar processos reais (spec §5: testes com mocks).
/// </summary>
public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken ct);
}

/// <summary>
/// Execução real de processos. SEM shell (UseShellExecute=false): os
/// argumentos passam tal-qual ao binário — a superfície de injeção é a
/// própria whitelist, já validada antes de chegar aqui.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        if (!process.Start())
            return new ProcessRunResult(-1, string.Empty, "Não foi possível iniciar o processo.", false);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var exitTask = process.WaitForExitAsync(ct);

        try
        {
            var completed = await Task.WhenAny(exitTask, Task.Delay(timeout));

            if (completed != exitTask)
            {
                // Timeout: mata a árvore do processo e reporta.
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best effort
                }

                try
                {
                    await exitTask;
                }
                catch
                {
                    // cancelado/timeout
                }

                return new ProcessRunResult(-1, await SafeRead(stdoutTask), await SafeRead(stderrTask), true);
            }

            return new ProcessRunResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask,
                false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // best effort
            }

            return new ProcessRunResult(-1, await SafeRead(stdoutTask), await SafeRead(stderrTask), false);
        }
    }

    private static async Task<string> SafeRead(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return string.Empty;
        }
    }
}

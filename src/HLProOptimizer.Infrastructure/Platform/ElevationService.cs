using System.Diagnostics;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HLProOptimizer.Infrastructure.Platform;

/// <summary>
/// Elevação sob demanda (o aplicativo usa manifest <c>asInvoker</c>).
/// </summary>
/// <remarks>
/// <para>
/// Quando uma operação exige administrador, este serviço re-executa o próprio
/// aplicativo com <c>--elevated-run &lt;operationId&gt; &lt;payloadFile&gt;
/// &lt;resultFile&gt; &lt;progressFile&gt;</c>. O processo elevado executa a
/// operação, escreve o resultado em JSON e encerra sem mostrar interface.
/// </para>
/// <para>
/// O progresso é transmitido por um arquivo de texto que o processo chamador
/// acompanha (tail), permitindo exibir o log em tempo real mesmo através da
/// fronteira de processos.
/// </para>
/// </remarks>
public sealed class ElevationService : IElevationService
{
    private const string ElevatedRunArgument = "--elevated-run";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ProgressPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly ISystemPaths _paths;
    private readonly IEnumerable<IElevatedOperation> _operations;
    private readonly ILogger<ElevationService> _logger;

    /// <summary>Cria o serviço de elevação.</summary>
    /// <param name="paths">Caminhos de dados (arquivos temporários de payload/resultado).</param>
    /// <param name="operations">Operações elevadas registradas (usadas quando já elevado).</param>
    /// <param name="logger">Logger.</param>
    public ElevationService(
        ISystemPaths paths,
        IEnumerable<IElevatedOperation> operations,
        ILogger<ElevationService> logger)
    {
        _paths = paths;
        _operations = operations;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsElevated => ElevationHelper.IsProcessElevated();

    /// <inheritdoc />
    public async Task<CommandResult> RunElevatedAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        if (IsElevated)
        {
            // Já elevado: executa diretamente, sem custo de novo processo.
            var operation = _operations.FirstOrDefault(o =>
                string.Equals(o.Id, ElevatedOperationIds.Command, StringComparison.OrdinalIgnoreCase));

            if (operation is not null)
            {
                var payload = JsonConvert.SerializeObject(new { FileName = fileName, Arguments = arguments, TimeoutSeconds = 600 });
                var json = await operation.ExecuteAsync(payload, null, cancellationToken).ConfigureAwait(false);

                return JsonConvert.DeserializeObject<CommandResult>(json)
                    ?? new CommandResult(fileName, arguments, -1, string.Empty, json, TimeSpan.Zero, true);
            }
        }

        var requestPayload = JsonConvert.SerializeObject(new { FileName = fileName, Arguments = arguments, TimeoutSeconds = 600 });
        var result = await RunElevatedOperationAsync(ElevatedOperationIds.Command, requestPayload, null, cancellationToken).ConfigureAwait(false);

        try
        {
            var commandResult = JsonConvert.DeserializeObject<CommandResult>(result);

            if (commandResult is not null)
            {
                return commandResult;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Resultado elevado não é um CommandResult válido: {Result}", Truncate(result));
        }

        return new CommandResult(fileName, arguments, -1, result, string.Empty, TimeSpan.Zero, true);
    }

    /// <inheritdoc />
    public async Task<string> RunElevatedOperationAsync(
        string operationId,
        string payloadJson,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("O identificador da operação é obrigatório.", nameof(operationId));
        }

        // Caminho rápido: já estamos elevados, então executa no próprio processo.
        if (IsElevated)
        {
            var local = _operations.FirstOrDefault(o =>
                string.Equals(o.Id, operationId, StringComparison.OrdinalIgnoreCase));

            if (local is null)
            {
                return JsonConvert.SerializeObject(new { Error = $"Operação desconhecida: {operationId}" });
            }

            progress?.Report(local.Description);

            return await local.ExecuteAsync(payloadJson ?? string.Empty, progress, cancellationToken).ConfigureAwait(false);
        }

        _paths.EnsureCreated();

        var token = Guid.NewGuid().ToString("N");
        var payloadFile = Path.Combine(_paths.TempDirectory, $"elev-{token}-payload.json");
        var resultFile = Path.Combine(_paths.TempDirectory, $"elev-{token}-result.json");
        var progressFile = Path.Combine(_paths.TempDirectory, $"elev-{token}-progress.log");

        try
        {
            await File.WriteAllTextAsync(payloadFile, payloadJson ?? string.Empty, cancellationToken).ConfigureAwait(false);
            File.WriteAllText(progressFile, string.Empty);

            var executable = _paths.ExecutablePath;

            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                return JsonConvert.SerializeObject(new { Error = "Não foi possível localizar o executável do aplicativo." });
            }

            var arguments = $"{ElevatedRunArgument} {operationId} \"{payloadFile}\" \"{resultFile}\" \"{progressFile}\"";

            _logger.LogInformation("Solicitando elevação para a operação '{OperationId}'.", operationId);
            progress?.Report("Solicitando privilégios de administrador (UAC)...");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            try
            {
                if (!process.Start())
                {
                    return JsonConvert.SerializeObject(new { Error = "Não foi possível iniciar o processo elevado." });
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                _logger.LogInformation("Usuário recusou o prompt de UAC para '{OperationId}'.", operationId);
                return JsonConvert.SerializeObject(new { Error = "Elevação recusada pelo usuário.", Cancelled = true });
            }

            using var tailCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tailTask = TailProgressAsync(progressFile, progress, tailCts.Token);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(OperationTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Timeout aguardando a operação elevada '{OperationId}'.", operationId);

                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Não foi possível encerrar o processo elevado.");
                }

                return JsonConvert.SerializeObject(new { Error = "Tempo limite excedido na operação elevada." });
            }

            tailCts.Cancel();

            try
            {
                await tailTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Esperado: o tail é interrompido ao final da operação.
            }

            var exitCode = process.ExitCode;

            if (!File.Exists(resultFile))
            {
                return JsonConvert.SerializeObject(new { Error = $"O processo elevado terminou sem produzir resultado (exit code {exitCode})." });
            }

            var result = await File.ReadAllTextAsync(resultFile, cancellationToken).ConfigureAwait(false);

            progress?.Report($"Operação elevada finalizada (exit code {exitCode}).");

            return result;
        }
        finally
        {
            DeleteQuietly(payloadFile);
            DeleteQuietly(resultFile);
            DeleteQuietly(progressFile);
        }
    }

    /// <inheritdoc />
    public Task<bool> RestartElevatedAsync(CancellationToken cancellationToken = default)
    {
        var executable = _paths.ExecutablePath;

        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            _logger.LogError("Não foi possível localizar o executável para reiniciar elevado.");
            return Task.FromResult(false);
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--elevated",
                    UseShellExecute = true,
                    Verb = "runas"
                }
            };

            var started = process.Start();

            return Task.FromResult(started);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.LogInformation("Usuário recusou a elevação ao reiniciar o aplicativo.");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao reiniciar o aplicativo de forma elevada.");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public string GetReason(string operation)
        => $"A operação \"{operation}\" altera configurações do Windows protegidas pelo sistema " +
           "(serviços, HKLM, arquivo hosts ou arquivos de sistema) e só pode ser executada por um administrador.";

    /// <summary>Acompanha o arquivo de progresso do processo elevado.</summary>
    private async Task TailProgressAsync(string progressFile, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return;
        }

        var offset = 0L;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(progressFile))
                {
                    using var stream = new FileStream(progressFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                    if (stream.Length > offset)
                    {
                        stream.Seek(offset, SeekOrigin.Begin);

                        using var reader = new StreamReader(stream);
                        string? line;

                        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                progress.Report(line);
                            }
                        }

                        offset = stream.Position;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Arquivo ainda bloqueado pelo processo elevado: tenta novamente.
            }

            await Task.Delay(ProgressPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Remove um arquivo temporário sem propagar erros.</summary>
    private void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível remover o arquivo temporário {Path}.", path);
        }
    }

    /// <summary>Trunca textos longos para o log.</summary>
    private static string Truncate(string value, int max = 300)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";
}

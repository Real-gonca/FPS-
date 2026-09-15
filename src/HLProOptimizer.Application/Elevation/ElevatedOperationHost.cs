using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Elevation;

/// <summary>
/// Host que executa operações elevadas quando o aplicativo é iniciado com
/// <c>--elevated-run</c>. Grava o resultado em JSON no arquivo informado e
/// transmite o progresso para um arquivo lido pelo processo chamador.
/// </summary>
public sealed class ElevatedOperationHost : IElevatedOperationHost
{
    private readonly IReadOnlyDictionary<string, IElevatedOperation> _operations;
    private readonly ILogger<ElevatedOperationHost> _logger;

    /// <summary>Cria o host.</summary>
    /// <param name="operations">Operações elevadas registradas no DI.</param>
    /// <param name="logger">Logger.</param>
    public ElevatedOperationHost(IEnumerable<IElevatedOperation> operations, ILogger<ElevatedOperationHost> logger)
    {
        _operations = operations.ToDictionary(
            o => o.Id,
            o => o,
            StringComparer.OrdinalIgnoreCase);

        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedOperations => _operations.Keys.ToList();

    /// <inheritdoc />
    public async Task<int> RunAsync(
        string operationId,
        string payloadFile,
        string resultFile,
        string? progressFile = null,
        CancellationToken cancellationToken = default)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            _logger.LogError("Operação elevada desconhecida: '{OperationId}'.", operationId);
            await WriteResultAsync(resultFile, JsonConvert.SerializeObject(new ElevatedOperationError(
                $"Operação desconhecida: {operationId}")), cancellationToken).ConfigureAwait(false);

            return 2;
        }

        _logger.LogInformation("Executando operação elevada '{OperationId}'.", operationId);

        using var progress = new FileProgressWriter(progressFile);

        try
        {
            var payload = File.Exists(payloadFile)
                ? await File.ReadAllTextAsync(payloadFile, cancellationToken).ConfigureAwait(false)
                : string.Empty;

            var result = await operation.ExecuteAsync(payload, progress, cancellationToken).ConfigureAwait(false);

            await WriteResultAsync(resultFile, result, cancellationToken).ConfigureAwait(false);

            return 0;
        }
        catch (OperationCanceledException)
        {
            await WriteResultAsync(resultFile, JsonConvert.SerializeObject(new ElevatedOperationError(
                "Operação cancelada.")), cancellationToken).ConfigureAwait(false);

            return 3;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na operação elevada '{OperationId}'.", operationId);

            await WriteResultAsync(resultFile, JsonConvert.SerializeObject(new ElevatedOperationError(
                $"{ex.GetType().Name}: {ex.Message}")), cancellationToken).ConfigureAwait(false);

            return 1;
        }
    }

    /// <summary>Grava o resultado (JSON) no arquivo de saída.</summary>
    private static async Task WriteResultAsync(string resultFile, string json, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(resultFile);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(resultFile, json, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Sem arquivo de resultado, o chamador tratará como timeout/falha.
        }
    }
}

/// <summary>Payload de erro retornado pelo host elevado.</summary>
/// <param name="Error">Mensagem de erro.</param>
public sealed record ElevatedOperationError(string Error);

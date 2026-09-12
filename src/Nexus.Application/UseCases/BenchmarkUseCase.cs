using Microsoft.Extensions.Logging;
using Nexus.Domain.Ports;

namespace Nexus.Application.UseCases;

/// <summary>
/// Caso de uso: correr o micro-benchmark A/B e guardar o resultado real
/// (persistido para os relatórios do patch 7). A UI tem de etiquetar o
/// resultado como micro-benchmark — este use case não fabrica conclusões
/// sobre "performance do sistema" (spec §0.2/§4.2).
/// </summary>
public sealed class BenchmarkUseCase
{
    private readonly IBenchmarkRunner _runner;
    private readonly IBenchmarkLog _log;
    private readonly ILogger<BenchmarkUseCase> _logger;

    public BenchmarkUseCase(IBenchmarkRunner runner, IBenchmarkLog log, ILogger<BenchmarkUseCase> logger)
    {
        _runner = runner;
        _log = log;
        _logger = logger;
    }

    public async Task<BenchmarkResult> RunAndSaveAsync(string context, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(ct);
        try
        {
            await _log.SaveAsync(result, context, ct);
        }
        catch (Exception ex)
        {
            // A medição é válida mesmo se a persistência falhar — não perder o dado.
            _logger.LogWarning(ex, "Não foi possível guardar o micro-benchmark ({Context}).", context);
        }
        return result;
    }
}

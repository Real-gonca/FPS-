using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Elevation.Operations;

/// <summary>Executa uma otimização completa em processo elevado.</summary>
public sealed class OptimizationElevatedOperation : IElevatedOperation
{
    private readonly IOptimizationService _optimizationService;

    /// <summary>Cria a operação.</summary>
    /// <param name="optimizationService">Orquestrador de otimização.</param>
    public OptimizationElevatedOperation(IOptimizationService optimizationService)
    {
        _optimizationService = optimizationService;
    }

    /// <inheritdoc />
    public string Id => ElevatedOperationIds.Optimization;

    /// <inheritdoc />
    public string Description => "Aplicar otimizações do sistema como administrador";

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string payloadJson, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var options = string.IsNullOrWhiteSpace(payloadJson)
            ? OptimizationOptions.ForFull()
            : JsonConvert.DeserializeObject<OptimizationOptions>(payloadJson) ?? OptimizationOptions.ForFull();

        var reporter = new Progress<ScanProgress>(p => progress?.Report(p.Message));

        var result = await _optimizationService.OptimizeAsync(options, reporter, cancellationToken).ConfigureAwait(false);

        return JsonConvert.SerializeObject(result);
    }
}

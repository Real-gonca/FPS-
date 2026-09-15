using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Elevation.Operations;

/// <summary>Executa uma limpeza em processo elevado (necessária para pastas de sistema).</summary>
public sealed class CleanupElevatedOperation : IElevatedOperation
{
    private readonly ICleanupService _cleanupService;

    /// <summary>Cria a operação.</summary>
    /// <param name="cleanupService">Serviço de limpeza.</param>
    public CleanupElevatedOperation(ICleanupService cleanupService)
    {
        _cleanupService = cleanupService;
    }

    /// <inheritdoc />
    public string Id => ElevatedOperationIds.Cleanup;

    /// <inheritdoc />
    public string Description => "Limpar arquivos do sistema como administrador";

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string payloadJson, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var request = JsonConvert.DeserializeObject<CleanupRequest>(payloadJson ?? string.Empty) ?? new CleanupRequest();

        var targets = await _cleanupService.GetTargetsAsync(request.Level, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<CleanupTarget> selected = request.TargetIds.Count == 0
            ? targets.Where(t => t.IsSafe).ToList()
            : targets.Where(t => request.TargetIds.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList();

        foreach (var target in selected)
        {
            target.IsSelected = true;
        }

        var reporter = new Progress<ScanProgress>(p => progress?.Report(p.Message));

        var result = await _cleanupService.CleanAsync(selected, reporter, cancellationToken).ConfigureAwait(false);

        return JsonConvert.SerializeObject(result);
    }

    /// <summary>Requisição da operação.</summary>
    public sealed class CleanupRequest
    {
        /// <summary>Nível de agressividade.</summary>
        public OptimizationLevel Level { get; set; } = OptimizationLevel.Balanced;

        /// <summary>Ids dos alvos selecionados (vazio = todos os seguros).</summary>
        public List<string> TargetIds { get; set; } = [];
    }
}

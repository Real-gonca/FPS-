using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>Limpa caches do sistema (thumbnails, shaders DirectX, icon cache, WU).</summary>
public sealed class SystemCacheStep : OptimizationStepBase
{
    private static readonly IssueCategory[] Categories =
    [
        IssueCategory.SystemCache,
        IssueCategory.WindowsUpdate,
        IssueCategory.LogsAndDumps
    ];

    private readonly ICleanupService _cleanupService;

    /// <summary>Cria o passo.</summary>
    /// <param name="cleanupService">Serviço de limpeza.</param>
    /// <param name="logger">Logger.</param>
    public SystemCacheStep(ICleanupService cleanupService, ILogger<SystemCacheStep> logger)
        : base(logger)
    {
        _cleanupService = cleanupService;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.SystemCache;

    /// <inheritdoc />
    public override string Name => "Limpar cache do sistema";

    /// <inheritdoc />
    public override string Description => "Remove miniaturas, cache de shaders DirectX, resíduos do Windows Update, logs e dumps.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Full,
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override int Order => 20;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.CleanSystemCache)
        {
            return (true, "Desativado nas opções.", 0);
        }

        var targets = await _cleanupService.GetTargetsAsync(context.Options.Level, cancellationToken).ConfigureAwait(false);

        var selected = targets.Where(t => Categories.Contains(t.Category)).ToList();
        selected.ForEach(t => t.IsSelected = true);

        if (selected.Count == 0)
        {
            return (true, "Nenhum cache do sistema acumulado.", 0);
        }

        var result = await _cleanupService.CleanAsync(selected, null, cancellationToken).ConfigureAwait(false);

        return (
            !result.HasFailures || result.DeletedFileCount > 0,
            $"{result.DeletedFileCount} arquivo(s) de cache removido(s).",
            result.DeletedBytes);
    }
}

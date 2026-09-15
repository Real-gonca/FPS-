using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>Limpa o cache dos navegadores instalados (não remove senhas, favoritos nem histórico).</summary>
public sealed class BrowserCacheStep : OptimizationStepBase
{
    private readonly ICleanupService _cleanupService;

    /// <summary>Cria o passo.</summary>
    /// <param name="cleanupService">Serviço de limpeza.</param>
    /// <param name="logger">Logger.</param>
    public BrowserCacheStep(ICleanupService cleanupService, ILogger<BrowserCacheStep> logger)
        : base(logger)
    {
        _cleanupService = cleanupService;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.BrowserCache;

    /// <inheritdoc />
    public override string Name => "Limpar cache dos navegadores";

    /// <inheritdoc />
    public override string Description => "Remove apenas cache de Chrome, Edge, Firefox, Brave e Opera - mantendo senhas e favoritos.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Full
    ];

    /// <inheritdoc />
    public override int Order => 30;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.CleanBrowserCache)
        {
            return (true, "Desativado nas opções.", 0);
        }

        var targets = await _cleanupService.GetTargetsAsync(context.Options.Level, cancellationToken).ConfigureAwait(false);

        var selected = targets
            .Where(t => t.Category == IssueCategory.BrowserCache && t.IsSafe)
            .ToList();

        selected.ForEach(t => t.IsSelected = true);

        if (selected.Count == 0)
        {
            return (true, "Nenhum navegador com cache acumulado.", 0);
        }

        var result = await _cleanupService.CleanAsync(selected, null, cancellationToken).ConfigureAwait(false);

        return (
            !result.HasFailures || result.DeletedFileCount > 0,
            $"{selected.Count} navegador(es) processado(s), {result.DeletedFileCount} arquivo(s) removido(s).",
            result.DeletedBytes);
    }
}

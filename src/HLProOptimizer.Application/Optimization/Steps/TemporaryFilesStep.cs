using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>Remove arquivos temporários do Windows e do usuário.</summary>
public sealed class TemporaryFilesStep : OptimizationStepBase
{
    private readonly ICleanupService _cleanupService;

    /// <summary>Cria o passo.</summary>
    /// <param name="cleanupService">Serviço de limpeza.</param>
    /// <param name="logger">Logger.</param>
    public TemporaryFilesStep(ICleanupService cleanupService, ILogger<TemporaryFilesStep> logger)
        : base(logger)
    {
        _cleanupService = cleanupService;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.TemporaryFiles;

    /// <inheritdoc />
    public override string Name => "Limpar arquivos temporários";

    /// <inheritdoc />
    public override string Description => "Remove o conteúdo de Windows\\Temp, %TEMP% e Prefetch.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Quick,
        OptimizationMode.Full,
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override int Order => 10;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.CleanTemporaryFiles)
        {
            return (true, "Desativado nas opções.", 0);
        }

        var targets = await _cleanupService.GetTargetsAsync(context.Options.Level, cancellationToken).ConfigureAwait(false);

        var selected = targets
            .Where(t => t.Category == IssueCategory.TemporaryFiles)
            .ToList();

        selected.ForEach(t => t.IsSelected = true);

        if (selected.Count == 0)
        {
            return (true, "Nenhum arquivo temporário encontrado.", 0);
        }

        var result = await _cleanupService.CleanAsync(selected, null, cancellationToken).ConfigureAwait(false);

        return (
            !result.HasFailures || result.DeletedFileCount > 0,
            $"{result.DeletedFileCount} arquivo(s) removido(s), {result.SkippedFileCount} em uso.",
            result.DeletedBytes);
    }
}

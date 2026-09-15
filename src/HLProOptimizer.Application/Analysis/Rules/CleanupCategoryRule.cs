using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Application.Analysis.Rules;

/// <summary>
/// Regra genérica que transforma os alvos de limpeza de uma categoria em
/// problemas de análise. Uma única implementação é registrada no DI para cada
/// categoria (DRY): arquivos temporários, cache do sistema, logs, Windows Update,
/// lixeira, cache de navegadores e registro.
/// </summary>
/// <remarks>
/// A regra não varre o disco: consome <see cref="AnalysisContext.CleanupTargets"/>,
/// pré-carregado uma única vez pelo <c>SystemAnalyzer</c> antes da execução
/// paralela das regras. Isso evita N varreduras duplicadas.
/// </remarks>
public sealed class CleanupCategoryRule : IAnalysisRule
{
    /// <summary>Acima deste volume o problema é considerado de gravidade alta.</summary>
    private const long HighSeverityBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Acima deste volume o problema é considerado de gravidade média.</summary>
    private const long MediumSeverityBytes = 256L * 1024 * 1024;

    private readonly ILocalizationService _localization;
    private readonly string _localizationKey;

    /// <summary>Cria a regra para uma categoria de limpeza.</summary>
    /// <param name="category">Categoria analisada.</param>
    /// <param name="localizationKey">Chave de localização do nome da categoria (ex.: "Cat_TemporaryFiles").</param>
    /// <param name="order">Ordem de execução no scan.</param>
    /// <param name="localization">Serviço de localização (o nome é resolvido sob demanda, refletindo o idioma ativo).</param>
    /// <param name="requiresAdmin">Se a categoria só é plenamente visível com administrador.</param>
    public CleanupCategoryRule(
        IssueCategory category,
        string localizationKey,
        int order,
        ILocalizationService localization,
        bool requiresAdmin = false)
    {
        Category = category;
        _localizationKey = localizationKey;
        _localization = localization;
        Order = order;
        RequiresAdmin = requiresAdmin;
        Id = $"analysis.cleanup.{category.ToString().ToLowerInvariant()}";
    }

    /// <summary>Nome legível da categoria, resolvido no idioma ativo.</summary>
    private string CategoryName => _localization[_localizationKey];

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string Name => $"Verificar {CategoryName}";

    /// <inheritdoc />
    public IssueCategory Category { get; }

    /// <inheritdoc />
    public int Order { get; }

    /// <inheritdoc />
    public bool RequiresAdmin { get; }

    /// <inheritdoc />
    public double Weight => Category is IssueCategory.Registry or IssueCategory.WindowsUpdate ? 0.8d : 1.0d;

    /// <inheritdoc />
    public Task<IReadOnlyList<AnalysisIssue>> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var targets = (context.CleanupTargets ?? [])
            .Where(t => t.Category == Category)
            .ToList();

        if (targets.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<AnalysisIssue>>([]);
        }

        var totalBytes = targets.Sum(t => t.EstimatedBytes);
        var totalFiles = targets.Sum(t => t.FileCount);

        // Nada a reportar quando o volume é irrelevante (< 1 MB).
        if (totalBytes < 1024 * 1024 && totalFiles == 0)
        {
            return Task.FromResult<IReadOnlyList<AnalysisIssue>>([]);
        }

        var severity = totalBytes switch
        {
            >= HighSeverityBytes => Severity.High,
            >= MediumSeverityBytes => Severity.Medium,
            _ => Severity.Low
        };

        var issue = new AnalysisIssue(
            Id: Id,
            Title: $"{CategoryName} acumulados",
            Description: BuildDescription(targets, totalBytes, totalFiles),
            Category: Category,
            Severity: severity,
            RecoverableBytes: totalBytes,
            RecommendedAction: "Limpar",
            CanAutoFix: true,
            RequiresAdmin: targets.Any(t => t.RequiresAdmin),
            ItemCount: totalFiles > 0 ? totalFiles : targets.Count,
            FixIdentifier: MapFixIdentifier());

        return Task.FromResult<IReadOnlyList<AnalysisIssue>>([issue]);
    }

    /// <summary>Monta a descrição com o detalhe dos principais alvos da categoria.</summary>
    private static string BuildDescription(IReadOnlyList<CleanupTarget> targets, long totalBytes, int totalFiles)
    {
        var top = targets
            .OrderByDescending(t => t.EstimatedBytes)
            .Take(3)
            .Select(t => $"{t.Name} ({ByteFormat.Format(t.EstimatedBytes)})");

        var detail = string.Join(", ", top);
        var filesText = totalFiles > 0 ? $"{totalFiles} arquivo(s)" : $"{targets.Count} local(is)";

        return $"{ByteFormat.Format(totalBytes)} em {filesText}: {detail}.";
    }

    /// <summary>Passo de otimização capaz de resolver esta categoria.</summary>
    private string MapFixIdentifier() => Category switch
    {
        IssueCategory.TemporaryFiles => Optimization.OptimizationStepIds.TemporaryFiles,
        IssueCategory.SystemCache or IssueCategory.WindowsUpdate or IssueCategory.LogsAndDumps => Optimization.OptimizationStepIds.SystemCache,
        IssueCategory.BrowserCache => Optimization.OptimizationStepIds.BrowserCache,
        IssueCategory.RecycleBin => Optimization.OptimizationStepIds.RecycleBin,
        IssueCategory.Registry => Optimization.OptimizationStepIds.Registry,
        _ => Optimization.OptimizationStepIds.TemporaryFiles
    };
}

using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Orquestrador do Scan Completo: executa as <see cref="IAnalysisRule"/> em
/// paralelo (TPL), reporta progresso por categoria e consolida o <see cref="ScanReport"/>.
/// </summary>
public interface ISystemAnalyzer
{
    /// <summary>Categorias que serão analisadas (na ordem de execução).</summary>
    IReadOnlyList<(IssueCategory Category, string Name)> Categories { get; }

    /// <summary>Executa o scan completo.</summary>
    /// <param name="progress">Progresso por categoria/etapa.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Relatório consolidado.</returns>
    Task<ScanReport> AnalyzeAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Executa apenas as regras de determinadas categorias.</summary>
    /// <param name="categories">Categorias desejadas.</param>
    /// <param name="progress">Progresso.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<ScanReport> AnalyzeAsync(
        IReadOnlyCollection<IssueCategory> categories,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Corrige automaticamente os problemas selecionados.</summary>
    /// <param name="issues">Problemas a corrigir.</param>
    /// <param name="progress">Progresso.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<OptimizationResult> FixAsync(
        IReadOnlyList<AnalysisIssue> issues,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

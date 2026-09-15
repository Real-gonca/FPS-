using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Fachada de limpeza usada pela UI: agrega todos os <see cref="ICleanupProvider"/>,
/// monta a árvore de categorias e executa a limpeza selecionada.
/// </summary>
public interface ICleanupService
{
    /// <summary>Executa o scan de todos os provedores e agrupa por categoria.</summary>
    /// <param name="level">Nível de agressividade.</param>
    /// <param name="progress">Progresso.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<CleanupCategoryGroup>> ScanAsync(
        OptimizationLevel level,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Limpa os alvos informados (normalmente os selecionados na UI).</summary>
    /// <param name="targets">Alvos a limpar.</param>
    /// <param name="progress">Progresso.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CleanupResult> CleanAsync(
        IReadOnlyList<CleanupTarget> targets,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Executa uma limpeza rápida das categorias mais comuns (Quick Action do Dashboard).</summary>
    /// <param name="progress">Progresso.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CleanupResult> QuickCleanAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Retorna apenas os alvos (sem agrupar), útil para a otimização.</summary>
    /// <param name="level">Nível de agressividade.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<CleanupTarget>> GetTargetsAsync(OptimizationLevel level, CancellationToken cancellationToken = default);
}

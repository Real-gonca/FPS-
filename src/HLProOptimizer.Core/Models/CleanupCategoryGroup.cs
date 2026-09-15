using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>Agrupamento de <see cref="CleanupTarget"/> por categoria (nó da TreeView).</summary>
/// <param name="Category">Categoria do grupo.</param>
/// <param name="DisplayName">Nome localizado do grupo.</param>
/// <param name="Targets">Alvos pertencentes ao grupo.</param>
public sealed record CleanupCategoryGroup(
    IssueCategory Category,
    string DisplayName,
    IReadOnlyList<CleanupTarget> Targets)
{
    /// <summary>Total de bytes do grupo (selecionados ou não).</summary>
    public long TotalBytes => Targets.Sum(t => t.EstimatedBytes);

    /// <summary>Bytes que serão liberados considerando apenas os itens marcados.</summary>
    public long SelectedBytes => Targets.Where(t => t.IsSelected).Sum(t => t.EstimatedBytes);

    /// <summary>Indica se todos os alvos do grupo estão marcados.</summary>
    public bool IsFullySelected => Targets.Count > 0 && Targets.All(t => t.IsSelected);

    /// <summary>Indica se o grupo está parcialmente marcado (estado indeterminado).</summary>
    public bool IsPartiallySelected => Targets.Any(t => t.IsSelected) && !IsFullySelected;
}

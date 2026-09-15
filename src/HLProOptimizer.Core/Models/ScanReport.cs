using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>
/// Resultado agregado de um Scan Completo, persistido em SQLite para o
/// "Ver Relatório Completo" do Dashboard.
/// </summary>
public sealed class ScanReport
{
    /// <summary>Identificador do relatório (GUID).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Início da análise.</summary>
    public DateTime StartedAt { get; init; } = DateTime.Now;

    /// <summary>Conclusão da análise.</summary>
    public DateTime CompletedAt { get; init; }

    /// <summary>Problemas encontrados.</summary>
    public IReadOnlyList<AnalysisIssue> Issues { get; init; } = [];

    /// <summary>Categorias que apresentaram falha durante o scan.</summary>
    public IReadOnlyList<string> FailedCategories { get; init; } = [];

    /// <summary>Espaço total recuperável em bytes.</summary>
    public long TotalRecoverableBytes => Issues.Sum(i => i.RecoverableBytes);

    /// <summary>Espaço total recuperável formatado.</summary>
    public string TotalRecoverableFormatted => ByteFormat.Format(TotalRecoverableBytes);

    /// <summary>Total de ganho de boot estimado em segundos.</summary>
    public double TotalBootImpactSeconds => Issues.Sum(i => i.EstimatedBootImpactSeconds);

    /// <summary>Problemas de severidade alta/crítica.</summary>
    public IReadOnlyList<AnalysisIssue> CriticalIssues =>
        Issues.Where(i => i.Severity >= Severity.High).OrderByDescending(i => i.Severity).ToList();

    /// <summary>Problemas agrupados por categoria, ordenados por severidade.</summary>
    public IReadOnlyDictionary<IssueCategory, IReadOnlyList<AnalysisIssue>> GroupedByCategory =>
        Issues
            .GroupBy(i => i.Category)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AnalysisIssue>)g.OrderByDescending(i => i.Severity).ThenByDescending(i => i.RecoverableBytes).ToList());

    /// <summary>Duração total do scan.</summary>
    public TimeSpan Duration => CompletedAt - StartedAt;
}

using HLOptimizerPro.Core.Models;

namespace HLOptimizerPro.App.Services;

/// <summary>
/// Guarda o último relatório de avaliação para ser reutilizado por várias páginas
/// (Avaliação → Recomendações → PC Boost) sem repetir a medição completa.
///
/// Validade: 30 minutos. Passado esse tempo, consideramos o relatório desatualizado e pedimos nova
/// avaliação — apresentar um número antigo como se fosse atual seria enganador.
/// </summary>
public sealed class AssessmentCache
{
    private static readonly TimeSpan ValidFor = TimeSpan.FromMinutes(30);

    public AssessmentReport? Last { get; private set; }

    public DateTime? LastRunAt { get; private set; }

    /// <summary>True quando existe um relatório recente que pode ser mostrado.</summary>
    public bool IsValid => Last is not null && LastRunAt is { } when && DateTime.Now - when < ValidFor;

    public void Store(AssessmentReport report)
    {
        Last = report;
        LastRunAt = DateTime.Now;
    }

    public void Clear()
    {
        Last = null;
        LastRunAt = null;
    }

    /// <summary>Explica ao utilizador a idade do relatório em cache.</summary>
    public string DescribeAge() => LastRunAt is { } when
        ? $"Relatório de {when:dd/MM/yyyy HH:mm} ({(DateTime.Now - when).TotalMinutes:0} min atrás)"
        : "Sem relatório em cache";
}

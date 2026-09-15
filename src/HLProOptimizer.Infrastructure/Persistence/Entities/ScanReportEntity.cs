namespace HLProOptimizer.Infrastructure.Persistence.Entities;

/// <summary>
/// Entidade persistida de um relatório de análise (tabela <c>ScanReports</c>).
/// </summary>
/// <remarks>
/// A lista de problemas é gravada como JSON: um relatório tem dezenas de
/// <c>AnalysisIssue</c> imutáveis que nunca são consultados individualmente no
/// banco (sempre carregamos o relatório inteiro). Normalizar em tabela filha
/// triplicaria o código sem nenhum benefício de consulta.
/// </remarks>
public class ScanReportEntity
{
    /// <summary>Identificador do relatório (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Início da análise (indexado para "mais recentes").</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Conclusão da análise.</summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>Problemas encontrados, serializados em JSON.</summary>
    public string IssuesJson { get; set; } = "[]";

    /// <summary>Categorias que falharam durante o scan, separadas por ponto e vírgula.</summary>
    public string FailedCategories { get; set; } = string.Empty;

    /// <summary>Espaço total recuperável (desnormalizado para consultas rápidas).</summary>
    public long TotalRecoverableBytes { get; set; }
}

namespace HLProOptimizer.Infrastructure.Persistence.Entities;

/// <summary>
/// Entidade persistida do histórico de ações (tabela <c>ActionRecords</c>).
/// </summary>
/// <remarks>
/// Espelho plano de <see cref="HLProOptimizer.Core.Models.ActionRecord"/>: a entidade existe para
/// que o EF Core mapeie colunas tipadas (índice por data e por tipo) sem acoplar o
/// modelo de domínio ao ORM. Enums são gravados como int (estáveis entre versões).
/// </remarks>
public class ActionRecordEntity
{
    /// <summary>Identificador (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Momento da ação (indexado para consultas recentes).</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Tipo da ação (<see cref="HLProOptimizer.Core.Enums.ActionKind"/>).</summary>
    public int Kind { get; set; }

    /// <summary>Título curto.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Detalhes/resultados legíveis.</summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>Bytes afetados (liberados/backup).</summary>
    public long BytesAffected { get; set; }

    /// <summary>Duração em milissegundos.</summary>
    public long DurationMs { get; set; }

    /// <summary>Se a ação foi bem-sucedida.</summary>
    public bool Success { get; set; }

    /// <summary>Marcadores adicionais em JSON.</summary>
    public string? PayloadJson { get; set; }
}

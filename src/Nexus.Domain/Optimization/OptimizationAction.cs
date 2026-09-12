namespace Nexus.Domain.Optimization;

/// <summary>
/// Uma ação de otimização aplicada (ou tentada), persistida no histórico
/// (SQLite) para auditabilidade e rollback individual (spec §4.10).
/// </summary>
public sealed class OptimizationAction
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Chave estável da tarefa (ex.: "telemetry-disable").</summary>
    public string TaskKey { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>O que a tarefa faz, porquê e como reverte — exibido na UI.</summary>
    public string Description { get; init; } = string.Empty;

    public RiskLevel Risk { get; init; }

    public OptimizationStatus Status { get; set; } = OptimizationStatus.Pending;

    /// <summary>Resultado legível (impacto, avisos, erros).</summary>
    public string ResultSummary { get; set; } = string.Empty;

    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FinishedUtc { get; set; }

    public long ElapsedMs { get; set; }

    /// <summary>Ids dos backups criados antes da aplicação (rollback usa estes).</summary>
    public List<Guid> BackupIds { get; set; } = new();
}

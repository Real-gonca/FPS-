using Nexus.Domain.Optimization;

namespace Nexus.Domain.Ports;

/// <summary>
/// Histórico de ações (auditabilidade total, spec §5): persiste todas as
/// otimizações aplicadas com timestamp, resultado e backups para rollback
/// individual (spec §4.10).
/// </summary>
public interface IHistoryStore
{
    Task AddAsync(OptimizationAction action, CancellationToken ct = default);

    Task UpdateAsync(OptimizationAction action, CancellationToken ct = default);

    Task<OptimizationAction?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Mais recentes primeiro.</summary>
    Task<IReadOnlyList<OptimizationAction>> QueryAsync(int take = 100, CancellationToken ct = default);
}

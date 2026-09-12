using Nexus.Domain.Optimization;

namespace Nexus.Domain.Ports;

/// <summary>
/// Aplicador de alterações com backup/rollback (pipeline Backup → Apply →
/// History → Rollback, spec §2.2).
/// </summary>
public interface IChangeApplier
{
    /// <summary>
    /// Cria um backup para cada descrição de alteração ANTES de qualquer
    /// escrita. Retorna os ids dos backups criados (o rollback usa estes ids).
    /// </summary>
    Task<IReadOnlyList<Guid>> BackupAsync(IEnumerable<ChangeDescriptor> changes, CancellationToken ct = default);

    /// <summary>Restauro em ordem inversa da criação (última alteração primeiro).</summary>
    Task RestoreAsync(IEnumerable<Guid> backupIds, CancellationToken ct = default);
}

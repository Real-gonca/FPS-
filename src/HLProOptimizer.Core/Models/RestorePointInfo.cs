namespace HLProOptimizer.Core.Models;

/// <summary>Ponto de restauração do Windows.</summary>
/// <param name="SequenceNumber">Número sequencial (identificador do WMI).</param>
/// <param name="Description">Descrição.</param>
/// <param name="CreationTime">Data de criação.</param>
/// <param name="RestorePointType">Tipo (APPLICATION_INSTALL, SYSTEM_CHECKPOINT...).</param>
public sealed record RestorePointInfo(
    long SequenceNumber,
    string Description,
    DateTime CreationTime,
    string RestorePointType);

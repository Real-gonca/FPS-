using HLProOptimizer.Core.Common;

namespace HLProOptimizer.Core.Models;

/// <summary>Resultado de uma operação de limpeza.</summary>
/// <param name="StartedAt">Início.</param>
/// <param name="CompletedAt">Fim.</param>
/// <param name="DeletedFileCount">Arquivos removidos.</param>
/// <param name="DeletedBytes">Bytes liberados.</param>
/// <param name="SkippedFileCount">Arquivos ignorados (em uso/protegidos).</param>
/// <param name="FailedFileCount">Arquivos com falha.</param>
/// <param name="Entries">Detalhe por alvo processado.</param>
public sealed record CleanupResult(
    DateTime StartedAt,
    DateTime CompletedAt,
    int DeletedFileCount,
    long DeletedBytes,
    int SkippedFileCount,
    int FailedFileCount,
    IReadOnlyList<CleanupEntryResult> Entries)
{
    /// <summary>Bytes liberados em formato legível.</summary>
    public string FreedFormatted => ByteFormat.Format(DeletedBytes);

    /// <summary>Duração da limpeza.</summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>Indica se houve alguma falha.</summary>
    public bool HasFailures => FailedFileCount > 0;

    /// <summary>Resultado vazio (operação cancelada antes de começar).</summary>
    public static CleanupResult Empty { get; } = new(DateTime.Now, DateTime.Now, 0, 0, 0, 0, []);
}

/// <summary>Resultado individual de um <see cref="CleanupTarget"/>.</summary>
/// <param name="TargetId">Id do alvo.</param>
/// <param name="TargetName">Nome do alvo.</param>
/// <param name="DeletedFiles">Arquivos removidos.</param>
/// <param name="DeletedBytes">Bytes liberados.</param>
/// <param name="SkippedFiles">Arquivos ignorados.</param>
/// <param name="Success">Se o alvo foi processado sem exceção.</param>
/// <param name="Error">Mensagem de erro, quando houver.</param>
public sealed record CleanupEntryResult(
    string TargetId,
    string TargetName,
    int DeletedFiles,
    long DeletedBytes,
    int SkippedFiles,
    bool Success,
    string? Error = null);

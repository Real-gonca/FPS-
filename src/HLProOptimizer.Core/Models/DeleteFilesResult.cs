namespace HLProOptimizer.Core.Models;

/// <summary>Resultado de uma exclusão em lote de arquivos.</summary>
/// <param name="DeletedCount">Arquivos removidos.</param>
/// <param name="DeletedBytes">Bytes liberados.</param>
/// <param name="SkippedCount">Arquivos ignorados (em uso/protegidos).</param>
/// <param name="FailedCount">Arquivos com falha.</param>
/// <param name="Errors">Primeiras mensagens de erro capturadas.</param>
public sealed record DeleteFilesResult(
    int DeletedCount,
    long DeletedBytes,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<string> Errors)
{
    /// <summary>Indica se houve sucesso total.</summary>
    public bool IsSuccess => FailedCount == 0;

    /// <summary>Resultado vazio.</summary>
    public static DeleteFilesResult Empty { get; } = new(0, 0, 0, 0, []);
}

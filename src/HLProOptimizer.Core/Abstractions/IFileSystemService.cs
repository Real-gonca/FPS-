using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Operações de arquivo usadas pela limpeza: enumeração, medição e exclusão
/// tolerante a falhas (arquivo em uso nunca derruba a operação inteira).
/// </summary>
public interface IFileSystemService
{
    /// <summary>Indica se um diretório existe.</summary>
    bool DirectoryExists(string path);

    /// <summary>Indica se um arquivo existe.</summary>
    bool FileExists(string path);

    /// <summary>Calcula o tamanho total de um diretório (recursivo).</summary>
    /// <param name="path">Caminho do diretório.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<long> GetDirectorySizeAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Conta os arquivos de um diretório (recursivo).</summary>
    /// <param name="path">Caminho do diretório.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<int> CountFilesAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Enumera arquivos de um diretório aplicando padrões (ex.: "*.tmp").</summary>
    /// <param name="path">Diretório raiz.</param>
    /// <param name="searchPatterns">Padrões de nome; vazio significa "*".</param>
    /// <param name="recursive">Se deve percorrer subpastas.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<string>> EnumerateFilesAsync(
        string path,
        IEnumerable<string>? searchPatterns = null,
        bool recursive = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exclui um conjunto de arquivos, ignorando falhas individuais.
    /// </summary>
    /// <param name="filePaths">Arquivos a excluir.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<DeleteFilesResult> DeleteFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default);

    /// <summary>Exclui o conteúdo de um diretório preservando a própria pasta.</summary>
    /// <param name="directoryPath">Diretório.</param>
    /// <param name="minimumAge">Ignora arquivos mais novos que esta idade (opcional).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<DeleteFilesResult> CleanDirectoryAsync(string directoryPath, TimeSpan? minimumAge = null, CancellationToken cancellationToken = default);

    /// <summary>Mede o tamanho da Lixeira.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<long> GetRecycleBinSizeAsync(CancellationToken cancellationToken = default);

    /// <summary>Esvazia a Lixeira (usa <c>SHEmptyRecycleBin</c> em modo silencioso).</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Bytes liberados (estimados).</returns>
    Task<long> EmptyRecycleBinAsync(CancellationToken cancellationToken = default);

    /// <summary>Exclui um arquivo único.</summary>
    /// <param name="filePath">Caminho.</param>
    /// <param name="error">Mensagem de erro, quando houver.</param>
    /// <returns>True quando excluído ou já inexistente.</returns>
    bool TryDeleteFile(string filePath, out string? error);
}

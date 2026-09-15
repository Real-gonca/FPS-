using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Metadados de arquivos binários (fabricante, versão, assinatura) e utilitários
/// de resolução de comandos. Implementado em Infrastructure com
/// <c>FileVersionInfo</c> e <c>Authenticode</c>.
/// </summary>
public interface IFileMetadataService
{
    /// <summary>Lê os metadados de um executável (null quando inacessível).</summary>
    /// <param name="filePath">Caminho do arquivo.</param>
    FileMetadata? GetMetadata(string filePath);

    /// <summary>
    /// Extrai o caminho do executável de um comando de inicialização
    /// (ex.: <c>"C:\App\a.exe" --min</c> → <c>C:\App\a.exe</c>), resolvendo
    /// variáveis de ambiente e caminhos relativos.
    /// </summary>
    /// <param name="command">Comando completo.</param>
    /// <returns>Caminho resolvido ou <c>null</c> quando não determinável.</returns>
    string? ResolveExecutablePath(string command);

    /// <summary>Abre o Explorer selecionando o arquivo/pasta informados.</summary>
    /// <param name="path">Caminho a revelar.</param>
    void RevealInExplorer(string path);

    /// <summary>Verifica se um binário é assinado digitalmente.</summary>
    /// <param name="filePath">Caminho do arquivo.</param>
    bool IsSigned(string filePath);
}

using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Acesso ao registro do Windows. Abstrai <c>Microsoft.Win32.Registry</c> para
/// que a lógica de negócio (Application) permaneça testável fora do Windows.
/// </summary>
public interface IRegistryService
{
    /// <summary>Indica se uma chave existe.</summary>
    /// <param name="hive">Colmeia.</param>
    /// <param name="keyPath">Caminho relativo à colmeia (ex.: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run").</param>
    bool KeyExists(RegistryHiveKind hive, string keyPath);

    /// <summary>Indica se um valor existe dentro de uma chave.</summary>
    bool ValueExists(RegistryHiveKind hive, string keyPath, string valueName);

    /// <summary>Lê um valor como string (null quando ausente ou de outro tipo).</summary>
    string? GetString(RegistryHiveKind hive, string keyPath, string valueName);

    /// <summary>Lê um valor DWORD como inteiro (null quando ausente).</summary>
    int? GetDword(RegistryHiveKind hive, string keyPath, string valueName);

    /// <summary>Lê um valor bruto (string, int, byte[], string[]...).</summary>
    object? GetValue(RegistryHiveKind hive, string keyPath, string valueName);

    /// <summary>Lê um valor binário (REG_BINARY) - usado pelo StartupApproved do Explorer.</summary>
    /// <param name="hive">Colmeia.</param>
    /// <param name="keyPath">Caminho da chave.</param>
    /// <param name="valueName">Nome do valor.</param>
    /// <returns>Bytes do valor ou <c>null</c> quando inexistente.</returns>
    byte[]? GetBinary(RegistryHiveKind hive, string keyPath, string valueName);

    /// <summary>Lista os nomes das subchaves.</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string keyPath);

    /// <summary>Lista os nomes dos valores de uma chave.</summary>
    IReadOnlyList<string> GetValueNames(RegistryHiveKind hive, string keyPath);

    /// <summary>Escreve um valor DWORD, criando a chave quando necessário.</summary>
    void SetDword(RegistryHiveKind hive, string keyPath, string valueName, int value);

    /// <summary>Escreve um valor string, criando a chave quando necessário.</summary>
    void SetString(RegistryHiveKind hive, string keyPath, string valueName, string value);

    /// <summary>Escreve um valor binário (REG_BINARY), criando a chave quando necessário.</summary>
    /// <param name="hive">Colmeia.</param>
    /// <param name="keyPath">Caminho da chave.</param>
    /// <param name="valueName">Nome do valor.</param>
    /// <param name="value">Bytes a gravar.</param>
    void SetBinary(RegistryHiveKind hive, string keyPath, string valueName, byte[] value);

    /// <summary>Remove um valor.</summary>
    /// <returns>True quando removido ou já inexistente.</returns>
    bool DeleteValue(RegistryHiveKind hive, string keyPath, string valueName);

    /// <summary>Remove uma chave e toda a sua árvore.</summary>
    /// <returns>True quando removida ou já inexistente.</returns>
    bool DeleteSubKeyTree(RegistryHiveKind hive, string keyPath);

    /// <summary>Exporta uma chave para um arquivo .reg (usa <c>reg export</c>).</summary>
    /// <param name="hive">Colmeia.</param>
    /// <param name="keyPath">Caminho da chave.</param>
    /// <param name="destinationFile">Arquivo .reg de destino.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> ExportKeyAsync(RegistryHiveKind hive, string keyPath, string destinationFile, CancellationToken cancellationToken = default);

    /// <summary>Importa um arquivo .reg (usa <c>reg import</c>, requer elevação para HKLM).</summary>
    /// <param name="regFile">Arquivo .reg.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> ImportKeyAsync(string regFile, CancellationToken cancellationToken = default);
}

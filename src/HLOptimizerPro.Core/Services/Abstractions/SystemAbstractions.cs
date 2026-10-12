using Microsoft.Win32;

namespace HLOptimizerPro.Core.Services.Abstractions;

/// <summary>Resultado da execução de um processo/comando externo.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    /// <summary>Saída consolidada (stdout + stderr), já sem linhas vazias.</summary>
    public string Output => string.Join(Environment.NewLine,
        new[] { StandardOutput, StandardError }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim()));
}

/// <summary>
/// Abstração sobre a execução de comandos externos (netsh, powercfg, reg, ipconfig...).
/// Permite substituir por duplos de teste nos testes unitários.
/// </summary>
public interface IProcessRunner
{
    Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default);

    CommandResult Run(string fileName, string arguments);

    bool FileExists(string fileName);
}

/// <summary>
/// Abstração mínima do registo do Windows (apenas leitura/escrita de valores e chaves)
/// para que os serviços de otimização sejam testáveis sem tocar no registo real.
/// </summary>
public interface IRegistryProvider
{
    /// <param name="hive">"HKLM", "HKCU", "HKCR".</param>
    /// <param name="subKey">Caminho sem a raiz.</param>
    /// <param name="name">Nome do valor (null para o valor predefinido).</param>
    object? GetValue(string hive, string subKey, string? name, object? defaultValue = null);

    /// <summary>Devolve true se a escrita foi possível (pode falhar por permissões).</summary>
    bool SetValue(string hive, string subKey, string? name, object value, RegistryValueKind kind = RegistryValueKind.DWord);

    bool DeleteValue(string hive, string subKey, string? name);

    bool KeyExists(string hive, string subKey);

    bool DeleteKey(string hive, string subKey, bool recursive = true);

    IReadOnlyList<string> GetSubKeyNames(string hive, string subKey);

    IReadOnlyList<string> GetValueNames(string hive, string subKey);

    /// <summary>Exporta a sub-árvore para um ficheiro .reg (backup antes de alterar).</summary>
    bool ExportKey(string hive, string subKey, string destinationFile);
}

/// <summary>
/// Sistema de ficheiros abstraído: permite testar a limpeza sem apagar ficheiros a sério
/// e medir quantos bytes foram realmente libertados.
/// </summary>
public interface IFileSystemProvider
{
    bool DirectoryExists(string path);
    bool FileExists(string path);

    /// <summary>Apaga o conteúdo de uma pasta (não a pasta em si) e devolve o resultado.</summary>
    Models.CleanupSummary ClearDirectory(string path, bool recursive = true, Predicate<string>? filter = null);

    long GetDirectorySize(string path);

    void DeleteFile(string path);

    void EnsureDirectory(string path);

    string GetTempPath();

    IReadOnlyList<string> EnumerateFiles(string path, bool recursive = true);

    void CopyFile(string source, string destination, bool overwrite = true);
}

/// <summary>Relógio injetável — torna determinísticos os testes de agendamento (limpeza automática).</summary>
public interface IClock
{
    DateTime Now { get; }
    DateTime UtcNow { get; }
}

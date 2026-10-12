using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Win32;

namespace HLOptimizerPro.Tests.Fakes;

/// <summary>
/// Registo do Windows em memória.
///
/// Os testes NUNCA tocam no registo real: escrever no registo de uma máquina de desenvolvimento
/// (ou de CI) para testar é inaceitável — e tornar-nos-ia incapazes de testar cenários destrutivos.
/// </summary>
public sealed class FakeRegistryProvider : IRegistryProvider
{
    private readonly Dictionary<string, Dictionary<string, object>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Valores escritos (para verificar que o modo Simular não escreve nada).</summary>
    public List<string> Writes { get; } = new();

    /// <summary>Chaves exportadas para .reg (verifica o backup antes de alterar).</summary>
    public List<string> Exports { get; } = new();

    /// <summary>Devolve true quando a escrita deve falhar (simula falta de privilégios).</summary>
    public bool FailWrites { get; set; }

    private static string Full(string hive, string subKey) => $"{hive}\\{subKey}";

    public void Seed(string hive, string subKey, string? name, object value)
    {
        var full = Full(hive, subKey);
        if (!_values.TryGetValue(full, out var values))
        {
            values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _values[full] = values;
        }

        values[name ?? string.Empty] = value;
        _keys[full] = new HashSet<string>(values.Keys, StringComparer.OrdinalIgnoreCase);
    }

    public object? GetValue(string hive, string subKey, string? name, object? defaultValue = null)
    {
        var full = Full(hive, subKey);
        return _values.TryGetValue(full, out var values) && values.TryGetValue(name ?? string.Empty, out var value)
            ? value
            : defaultValue;
    }

    public bool SetValue(string hive, string subKey, string? name, object value, RegistryValueKind kind = RegistryValueKind.DWord)
    {
        if (FailWrites) return false;

        var full = Full(hive, subKey);
        if (!_values.TryGetValue(full, out var values))
        {
            values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            _values[full] = values;
        }

        values[name ?? string.Empty] = value;
        _keys[full] = new HashSet<string>(values.Keys, StringComparer.OrdinalIgnoreCase);
        Writes.Add($"{full}\\{name}={value}");

        return true;
    }

    public bool DeleteValue(string hive, string subKey, string? name)
    {
        if (FailWrites) return false;

        var full = Full(hive, subKey);
        if (_values.TryGetValue(full, out var values)) values.Remove(name ?? string.Empty);

        Writes.Add($"DELETE {full}\\{name}");
        return true;
    }

    public bool KeyExists(string hive, string subKey) => _values.ContainsKey(Full(hive, subKey));

    public bool DeleteKey(string hive, string subKey, bool recursive = true)
    {
        _values.Remove(Full(hive, subKey));
        return true;
    }

    public IReadOnlyList<string> GetSubKeyNames(string hive, string subKey)
    {
        var prefix = Full(hive, subKey) + "\\";
        return _values.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..])
            .ToList();
    }

    public IReadOnlyList<string> GetValueNames(string hive, string subKey)
        => _values.TryGetValue(Full(hive, subKey), out var values) ? values.Keys.ToList() : Array.Empty<string>();

    public bool ExportKey(string hive, string subKey, string destinationFile)
    {
        Exports.Add(destinationFile);
        return true;
    }
}

/// <summary>
/// Sistema de ficheiros em memória: permite testar a limpeza sem apagar nada real e medir
/// exatamente quantos bytes seriam libertados.
/// </summary>
public sealed class FakeFileSystemProvider : IFileSystemProvider
{
    private readonly Dictionary<string, long> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Diretorias que o código tentou limpar (auditoria: garante que nunca toca no utilizador).</summary>
    public List<string> ClearedDirectories { get; } = new();

    public void SeedDirectory(string path, long sizeBytes)
    {
        _directories[path] = sizeBytes;
        _files.Add(Path.Combine(path, "ficheiro.tmp"));
    }

    public bool DirectoryExists(string path) => _directories.ContainsKey(path);

    public bool FileExists(string path) => _files.Contains(path);

    public CleanupSummary ClearDirectory(string path, bool recursive = true, Predicate<string>? filter = null)
    {
        ClearedDirectories.Add(path);

        var summary = new CleanupSummary();

        if (!_directories.TryGetValue(path, out var size))
            return summary;

        summary.FilesDeleted = 1;
        summary.BytesFreed = size;
        summary.Details.Add($"{path}: 1 ficheiro, {size} bytes");

        _directories.Remove(path);
        _files.RemoveWhere(f => f.StartsWith(path, StringComparison.OrdinalIgnoreCase));

        return summary;
    }

    public long GetDirectorySize(string path) => _directories.TryGetValue(path, out var size) ? size : 0;

    public void DeleteFile(string path) => _files.Remove(path);

    public void EnsureDirectory(string path) => _directories.TryAdd(path, 0);

    public string GetTempPath() => @"C:\Temp";

    public IReadOnlyList<string> EnumerateFiles(string path, bool recursive = true)
        => _files.Where(f => f.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToList();

    public void CopyFile(string source, string destination, bool overwrite = true) => _files.Add(destination);
}

/// <summary>Execução de comandos simulada: devolve o que os testes definirem, sem executar nada.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, CommandResult> _responses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Comandos executados (com argumentos), para auditoria nos testes.</summary>
    public List<string> Executed { get; } = new();

    public CommandResult Default { get; set; } = new(0, string.Empty, string.Empty);

    public void When(string fileName, string output, int exitCode = 0)
        => _responses[fileName] = new CommandResult(exitCode, output, string.Empty);

    public Task<CommandResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
        => Task.FromResult(Run(fileName, arguments));

    public CommandResult Run(string fileName, string arguments)
    {
        Executed.Add($"{fileName} {arguments}".Trim());
        return _responses.TryGetValue(fileName, out var result) ? result : Default;
    }

    public bool FileExists(string fileName) => true;
}

/// <summary>Relógio fixo: torna determinísticos os testes de agendamento e de datas.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTime? now = null) => Now = now ?? new DateTime(2026, 1, 15, 10, 30, 0);

    public DateTime Now { get; set; }

    public DateTime UtcNow => Now.ToUniversalTime();
}

/// <summary>Sensores ausentes (máquina virtual/CI): a aplicação tem de continuar a funcionar.</summary>
public sealed class NullSensorProvider : IHardwareSensorProvider
{
    public bool IsAvailable => false;

    public string? UnavailableReason => "Sensores não disponíveis (teste).";

    public void Initialize()
    {
    }

    public HardwareSensorSnapshot Snapshot() => HardwareSensorSnapshot.Empty;

    public void Dispose()
    {
    }
}

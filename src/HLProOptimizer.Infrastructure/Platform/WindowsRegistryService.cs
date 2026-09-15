using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Infrastructure.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HLProOptimizer.Infrastructure.Platform;

/// <summary>
/// Implementação de <see cref="IRegistryService"/> sobre <c>Microsoft.Win32.Registry</c>.
/// </summary>
/// <remarks>
/// Leituras nunca lançam exceção (retornam null/lista vazia), enquanto escritas
/// propagam <see cref="UnauthorizedAccessException"/> para que as camadas
/// superiores possam solicitar elevação.
/// </remarks>
public sealed class WindowsRegistryService : IRegistryService
{
    private readonly ICommandRunner _commandRunner;
    private readonly ILogger<WindowsRegistryService> _logger;

    /// <summary>Cria o serviço de registro.</summary>
    /// <param name="commandRunner">Executor usado pelo export/import (reg.exe).</param>
    /// <param name="logger">Logger.</param>
    public WindowsRegistryService(ICommandRunner commandRunner, ILogger<WindowsRegistryService> logger)
    {
        _commandRunner = commandRunner;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool KeyExists(RegistryHiveKind hive, string keyPath)
    {
        try
        {
            using var key = OpenBase(hive).OpenSubKey(Normalize(keyPath), writable: false);
            return key is not null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao verificar a existência de {Hive}\\{Key}.", hive, keyPath);
            return false;
        }
    }

    /// <inheritdoc />
    public bool ValueExists(RegistryHiveKind hive, string keyPath, string valueName)
    {
        try
        {
            using var key = OpenBase(hive).OpenSubKey(Normalize(keyPath), writable: false);
            return key?.GetValue(valueName) is not null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao verificar o valor {Value} em {Hive}\\{Key}.", valueName, hive, keyPath);
            return false;
        }
    }

    /// <inheritdoc />
    public string? GetString(RegistryHiveKind hive, string keyPath, string valueName)
    {
        var value = GetValue(hive, keyPath, valueName);

        return value switch
        {
            string text => text,
            null => null,
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    /// <inheritdoc />
    public int? GetDword(RegistryHiveKind hive, string keyPath, string valueName)
    {
        var value = GetValue(hive, keyPath, valueName);

        return value switch
        {
            int number => number,
            long number => unchecked((int)number),
            uint number => unchecked((int)number),
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    /// <inheritdoc />
    public object? GetValue(RegistryHiveKind hive, string keyPath, string valueName)
    {
        try
        {
            using var key = OpenBase(hive).OpenSubKey(Normalize(keyPath), writable: false);
            return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao ler {Hive}\\{Key}\\{Value}.", hive, keyPath, valueName);
            return null;
        }
    }

    /// <inheritdoc />
    public byte[]? GetBinary(RegistryHiveKind hive, string keyPath, string valueName)
        => GetValue(hive, keyPath, valueName) as byte[];

    /// <inheritdoc />
    public IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string keyPath)
    {
        try
        {
            using var key = OpenBase(hive).OpenSubKey(Normalize(keyPath), writable: false);
            return key?.GetSubKeyNames() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao enumerar subchaves de {Hive}\\{Key}.", hive, keyPath);
            return [];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetValueNames(RegistryHiveKind hive, string keyPath)
    {
        try
        {
            using var key = OpenBase(hive).OpenSubKey(Normalize(keyPath), writable: false);
            return key?.GetValueNames() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao enumerar valores de {Hive}\\{Key}.", hive, keyPath);
            return [];
        }
    }

    /// <inheritdoc />
    public void SetDword(RegistryHiveKind hive, string keyPath, string valueName, int value)
        => Write(hive, keyPath, valueName, value, RegistryValueKind.DWord);

    /// <inheritdoc />
    public void SetString(RegistryHiveKind hive, string keyPath, string valueName, string value)
        => Write(hive, keyPath, valueName, value, RegistryValueKind.String);

    /// <inheritdoc />
    public void SetBinary(RegistryHiveKind hive, string keyPath, string valueName, byte[] value)
        => Write(hive, keyPath, valueName, value, RegistryValueKind.Binary);

    /// <inheritdoc />
    public bool DeleteValue(RegistryHiveKind hive, string keyPath, string valueName)
    {
        try
        {
            using var key = OpenBase(hive).OpenSubKey(Normalize(keyPath), writable: true);

            if (key is null)
            {
                return true;
            }

            key.DeleteValue(valueName, throwOnMissingValue: false);
            _logger.LogDebug("Valor removido: {Hive}\\{Key}\\{Value}.", hive, keyPath, valueName);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao remover {Hive}\\{Key}\\{Value}.", hive, keyPath, valueName);
            return false;
        }
    }

    /// <inheritdoc />
    public bool DeleteSubKeyTree(RegistryHiveKind hive, string keyPath)
    {
        try
        {
            using var baseKey = OpenBase(hive);
            baseKey.DeleteSubKeyTree(Normalize(keyPath), throwOnMissingSubKey: false);
            _logger.LogDebug("Árvore removida: {Hive}\\{Key}.", hive, keyPath);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao remover a árvore {Hive}\\{Key}.", hive, keyPath);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExportKeyAsync(
        RegistryHiveKind hive,
        string keyPath,
        string destinationFile,
        CancellationToken cancellationToken = default)
    {
        var fullPath = $"{HiveName(hive)}\\{Normalize(keyPath)}";
        var directory = Path.GetDirectoryName(destinationFile);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var result = await _commandRunner.RunAsync(
            "reg.exe",
            $"export \"{fullPath}\" \"{destinationFile}\" /y",
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Falha ao exportar {Key}: {Error}", fullPath, result.CombinedOutput);
        }

        return result.IsSuccess;
    }

    /// <inheritdoc />
    public async Task<bool> ImportKeyAsync(string regFile, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(regFile))
        {
            _logger.LogWarning("Arquivo .reg não encontrado: {File}.", regFile);
            return false;
        }

        var result = await _commandRunner.RunAsync(
            "reg.exe",
            $"import \"{regFile}\"",
            cancellationToken,
            elevated: !ElevationHelper.IsProcessElevated()).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Falha ao importar {File}: {Error}", regFile, result.CombinedOutput);
        }

        return result.IsSuccess;
    }

    /// <summary>Grava um valor criando a chave quando necessário.</summary>
    private void Write(RegistryHiveKind hive, string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        using var key = OpenBase(hive).CreateSubKey(Normalize(keyPath), writable: true);

        if (key is null)
        {
            throw new InvalidOperationException($"Não foi possível criar a chave {HiveName(hive)}\\{keyPath}.");
        }

        key.SetValue(valueName, value, kind);

        _logger.LogDebug("Valor gravado: {Hive}\\{Key}\\{Value} = {Data} ({Kind}).", hive, keyPath, valueName, value, kind);
    }

    /// <summary>Abre a colmeia correspondente.</summary>
    private static RegistryKey OpenBase(RegistryHiveKind hive) => hive switch
    {
        RegistryHiveKind.ClassesRoot => Registry.ClassesRoot,
        RegistryHiveKind.CurrentUser => Registry.CurrentUser,
        RegistryHiveKind.LocalMachine => Registry.LocalMachine,
        RegistryHiveKind.Users => Registry.Users,
        RegistryHiveKind.CurrentConfig => Registry.CurrentConfig,
        _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, "Colmeia não suportada.")
    };

    /// <summary>Nome textual da colmeia (para reg.exe e logs).</summary>
    private static string HiveName(RegistryHiveKind hive) => hive switch
    {
        RegistryHiveKind.ClassesRoot => "HKCR",
        RegistryHiveKind.CurrentUser => "HKCU",
        RegistryHiveKind.LocalMachine => "HKLM",
        RegistryHiveKind.Users => "HKU",
        RegistryHiveKind.CurrentConfig => "HKCC",
        _ => hive.ToString()
    };

    /// <summary>Remove prefixos ("HKLM\", barras invertidas iniciais) e padroniza o caminho.</summary>
    private static string Normalize(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return string.Empty;
        }

        var value = keyPath.Trim();

        foreach (var prefix in new[] { "HKEY_LOCAL_MACHINE\\", "HKEY_CURRENT_USER\\", "HKLM\\", "HKCU\\", "HKCR\\", "HKU\\" })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
                break;
            }
        }

        return value.Trim('\\');
    }
}

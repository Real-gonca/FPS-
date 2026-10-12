using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HLOptimizerPro.Core.Services.System;

/// <summary>
/// Implementação real do acesso ao registo do Windows.
/// Todas as escritas são tolerantes a falhas de permissões: devolvem <c>false</c> em vez de
/// lançar exceção, para que a UI possa explicar o problema ao utilizador.
/// </summary>
public sealed class WindowsRegistryProvider : IRegistryProvider
{
    private readonly ILogger<WindowsRegistryProvider> _logger;

    public WindowsRegistryProvider(ILogger<WindowsRegistryProvider> logger) => _logger = logger;

    public object? GetValue(string hive, string subKey, string? name, object? defaultValue = null)
    {
        try
        {
            using var key = Open(hive, subKey, writable: false);
            return key?.GetValue(name ?? string.Empty, defaultValue);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Leitura falhou: {Hive}\\{SubKey}\\{Name}", hive, subKey, name);
            return defaultValue;
        }
    }

    public bool SetValue(string hive, string subKey, string? name, object value, RegistryValueKind kind = RegistryValueKind.DWord)
    {
        try
        {
            using var key = Open(hive, subKey, writable: true, createIfMissing: true);
            if (key is null) return false;

            key.SetValue(name ?? string.Empty, value, kind);
            _logger.LogDebug("Registo escrito: {Hive}\\{SubKey}\\{Name} = {Value}", hive, subKey, name, value);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Escrita falhou (permissões?): {Hive}\\{SubKey}\\{Name}", hive, subKey, name);
            return false;
        }
    }

    public bool DeleteValue(string hive, string subKey, string? name)
    {
        try
        {
            using var key = Open(hive, subKey, writable: true);
            if (key is null) return false;

            key.DeleteValue(name ?? string.Empty, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remoção de valor falhou: {Hive}\\{SubKey}\\{Name}", hive, subKey, name);
            return false;
        }
    }

    public bool KeyExists(string hive, string subKey)
    {
        try
        {
            using var key = Open(hive, subKey, writable: false);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool DeleteKey(string hive, string subKey, bool recursive = true)
    {
        try
        {
            var root = ResolveRoot(hive);
            root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);

            if (!recursive)
            {
                // Com recursive=false apenas removemos se ficou vazio.
                using var key = root.OpenSubKey(subKey);
                if (key is null) return true;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remoção de chave falhou: {Hive}\\{SubKey}", hive, subKey);
            return false;
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(string hive, string subKey)
    {
        try
        {
            using var key = Open(hive, subKey, writable: false);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Listagem de subchaves falhou: {Hive}\\{SubKey}", hive, subKey);
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<string> GetValueNames(string hive, string subKey)
    {
        try
        {
            using var key = Open(hive, subKey, writable: false);
            return key?.GetValueNames() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Exporta a chave para .reg usando <c>reg.exe export</c> — é o único mecanismo fiável de backup
    /// (a API .NET não exporta recursivamente). Devolve false se o ficheiro não foi criado.
    /// </summary>
    public bool ExportKey(string hive, string subKey, string destinationFile)
    {
        try
        {
            var directory = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var full = $"{hive}\\{subKey}";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"export \"{full}\" \"{destinationFile}\" /y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(15_000);

            var ok = process.ExitCode == 0 && File.Exists(destinationFile);
            if (!ok) _logger.LogWarning("Exportação do registo falhou: {Key}", full);
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exportação do registo falhou: {Hive}\\{SubKey}", hive, subKey);
            return false;
        }
    }

    private static RegistryKey ResolveRoot(string hive) => hive.ToUpperInvariant() switch
    {
        "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
        "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
        "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
        "HKU" or "HKEY_USERS" => Registry.Users,
        "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
        _ => throw new ArgumentException($"Colmeia desconhecida: {hive}", nameof(hive)),
    };

    private RegistryKey? Open(string hive, string subKey, bool writable, bool createIfMissing = false)
    {
        var root = ResolveRoot(hive);

        if (createIfMissing && writable)
            return root.CreateSubKey(subKey, writable: true);

        return root.OpenSubKey(subKey, writable);
    }
}

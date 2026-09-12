using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Registry;

/// <summary>
/// Acesso real ao registry do Windows (Microsoft.Win32.Registry).
///
/// Resiliência (spec §5): falhas de probe (chave inexistente, não-Windows)
/// devolvem "inexistente" sem lançar; falhas de escrita (permissões) lançam
/// exceção controlada, que o orquestrador converte em rollback + erro.
/// </summary>
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    private readonly ILogger<WindowsRegistryAccess> _log;

    public WindowsRegistryAccess(ILogger<WindowsRegistryAccess> log) => _log = log;

    public Task<RegistryProbe> ProbeAsync(string path, string valueName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using RegistryKey? key = Open(path, writable: false);
            if (key is null)
                return Task.FromResult(new RegistryProbe(false, null, null));

            return Task.FromResult(Read(key, valueName));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Probe de {Path}\\{Name} falhou — tratado como inexistente.", path, valueName);
            return Task.FromResult(new RegistryProbe(false, null, null));
        }
    }

    public Task SetAsync(string path, string valueName, RegistryValueKind kind, object value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            RegistryKey? key = null;
            try
            {
                key = Open(path, writable: true)
                    ?? throw new DirectoryNotFoundException($"Caminho de registry não encontrado: {path}");
                key.SetValue(valueName, value, MapKind(kind));
                _log.LogInformation("Registry definido: {Path}\\{Name} = {Value}", path, valueName, value);
            }
            finally
            {
                key?.Dispose();
            }
        }, ct);
    }

    public Task DeleteAsync(string path, string valueName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            RegistryKey? key = null;
            try
            {
                key = Open(path, writable: true)
                    ?? throw new DirectoryNotFoundException($"Caminho de registry não encontrado: {path}");
                key.DeleteValue(valueName, throwOnMissingValue: false);
                _log.LogInformation("Valor de registry removido: {Path}\\{Name}", path, valueName);
            }
            finally
            {
                key?.Dispose();
            }
        }, ct);
    }

    private static RegistryValueKind MapKind(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.Dword => Microsoft.Win32.RegistryValueKind.DWord,
        RegistryValueKind.Qword => Microsoft.Win32.RegistryValueKind.QWord,
        RegistryValueKind.MultiString => Microsoft.Win32.RegistryValueKind.MultiString,
        RegistryValueKind.Binary => Microsoft.Win32.RegistryValueKind.Binary,
        _ => Microsoft.Win32.RegistryValueKind.String,
    };

    private static RegistryProbe Read(RegistryKey key, string valueName)
    {
        if (key.GetValue(valueName) is not { } raw)
            return new RegistryProbe(false, null, null);

        (RegistryValueKind? kind, object? value) = key.GetValueKind(valueName) switch
        {
            Microsoft.Win32.RegistryValueKind.DWord => (RegistryValueKind.Dword, (int)raw),
            Microsoft.Win32.RegistryValueKind.QWord => (RegistryValueKind.Qword, (long)raw),
            Microsoft.Win32.RegistryValueKind.MultiString => (RegistryValueKind.MultiString, (string[])raw),
            Microsoft.Win32.RegistryValueKind.Binary => (RegistryValueKind.Binary, (byte[])raw),
            _ => (RegistryValueKind.String, raw as string),
        };
        return new RegistryProbe(true, kind, value);
    }

    /// <summary>
    /// Abre a chave do caminho "HKLM\...". Em escrita usa CreateSubKey
    /// (cria a cadeia se não existir — comportamento documentado: a tarefa
    /// de telemetria pode criar a chave de políticas numa máquina limpa).
    /// </summary>
    private static RegistryKey? Open(string path, bool writable)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var (hive, subPath) = SplitHive(path);
        RegistryKey baseKey = Registry.OpenBaseKey(hive, RegistryView.Default, RegistryKeyPermissionCheck.ReadWriteSubKey);
        return writable
            ? baseKey.CreateSubKey(subPath)
            : baseKey.OpenSubKey(subPath);
    }

    private static (RegistryHive Hive, string SubPath) SplitHive(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int idx = path.IndexOf('\\');
        if (idx <= 0)
            throw new ArgumentException($"Caminho de registry inválido: '{path}' (esperado 'HKLM\\...' ou 'HKCU\\...').", nameof(path));

        string hiveName = path[..idx].ToUpperInvariant();
        string sub = path[(idx + 1)..].TrimEnd('\\');

        var hive = hiveName switch
        {
            "HKLM" => RegistryHive.LocalMachine,
            "HKCU" => RegistryHive.CurrentUser,
            "HKCR" => RegistryHive.ClassesRoot,
            _ => throw new ArgumentException($"Hive não suportado: '{hiveName}'.", nameof(path)),
        };
        return (hive, sub);
    }
}

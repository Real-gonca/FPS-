using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Utilities;
using Microsoft.Win32;

namespace HL.Optimizer.Pro.Core.Services;

public class RegistryService : IRegistryService
{
    public async Task<object?> GetValueAsync(string keyPath, string valueName)
    {
        return await Task.Run(() =>
        {
            try
            {
                var hive = GetHive(ref keyPath);
                return RegistryHelper.GetValue(keyPath, valueName, hive);
            }
            catch { return null; }
        });
    }

    public async Task<bool> SetValueAsync(string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        return await Task.Run(() =>
        {
            try
            {
                var hive = GetHive(ref keyPath);
                return RegistryHelper.SetValue(keyPath, valueName, value, kind, hive);
            }
            catch { return false; }
        });
    }

    public async Task<bool> DeleteValueAsync(string keyPath, string valueName)
    {
        return await Task.Run(() =>
        {
            try
            {
                var hive = GetHive(ref keyPath);
                return RegistryHelper.DeleteValue(keyPath, valueName, hive);
            }
            catch { return false; }
        });
    }

    public async Task<bool> KeyExistsAsync(string keyPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var hive = GetHive(ref keyPath);
                return RegistryHelper.KeyExists(keyPath, hive);
            }
            catch { return false; }
        });
    }

    private RegistryHive GetHive(ref string path)
    {
        if (path.StartsWith("HKEY_LOCAL_MACHINE\\"))
        {
            path = path.Substring("HKEY_LOCAL_MACHINE\\".Length);
            return RegistryHive.LocalMachine;
        }
        if (path.StartsWith("HKEY_CURRENT_USER\\"))
        {
            path = path.Substring("HKEY_CURRENT_USER\\".Length);
            return RegistryHive.CurrentUser;
        }
        if (path.StartsWith("HKEY_CLASSES_ROOT\\"))
        {
            path = path.Substring("HKEY_CLASSES_ROOT\\".Length);
            return RegistryHive.ClassesRoot;
        }
        if (path.StartsWith("HKEY_CURRENT_CONFIG\\"))
        {
            path = path.Substring("HKEY_CURRENT_CONFIG\\".Length);
            return RegistryHive.CurrentConfig;
        }
        return RegistryHive.LocalMachine;
    }
}

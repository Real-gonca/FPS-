using Microsoft.Win32;

namespace HL.Optimizer.Pro.Core.Utilities;

public static class RegistryHelper
{
    public static object? GetValue(string keyPath, string valueName, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(keyPath);
            return key?.GetValue(valueName);
        }
        catch { return null; }
    }

    public static bool SetValue(string keyPath, string valueName, object value, RegistryValueKind kind, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.CreateSubKey(keyPath, true);
            if (key == null) return false;
            key.SetValue(valueName, value, kind);
            return true;
        }
        catch { return false; }
    }

    public static bool DeleteValue(string keyPath, string valueName, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(keyPath, true);
            if (key == null) return false;
            key.DeleteValue(valueName, false);
            return true;
        }
        catch { return false; }
    }

    public static bool KeyExists(string keyPath, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(keyPath);
            return key != null;
        }
        catch { return false; }
    }

    public static Dictionary<string, object?> GetAllValues(string keyPath, RegistryHive hive = RegistryHive.LocalMachine)
    {
        var dict = new Dictionary<string, object?>();
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(keyPath);
            if (key == null) return dict;
            foreach (var name in key.GetValueNames())
            {
                dict[name] = key.GetValue(name);
            }
        }
        catch { }
        return dict;
    }
}

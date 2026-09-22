using System.Management;

namespace HL.Optimizer.Pro.Core.Utilities;

public static class WmiHelper
{
    public static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
    {
        var result = new List<ManagementBaseObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                result.Add(obj);
            }
        }
        catch { }
        return result;
    }

    public static string GetPropertyString(ManagementBaseObject obj, string property, string defaultValue = "")
    {
        try
        {
            var val = obj[property];
            return val?.ToString() ?? defaultValue;
        }
        catch { return defaultValue; }
    }

    public static T GetProperty<T>(ManagementBaseObject obj, string property, T defaultValue)
    {
        try
        {
            var val = obj[property];
            if (val == null) return defaultValue;
            return (T)Convert.ChangeType(val, typeof(T));
        }
        catch { return defaultValue; }
    }
}

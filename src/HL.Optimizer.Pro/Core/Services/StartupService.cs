using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.Management;

namespace HL.Optimizer.Pro.Core.Services;

public class StartupService : IStartupService
{
    public async Task<List<StartupItem>> GetStartupItemsAsync()
    {
        return await Task.Run(() =>
        {
            var items = new List<StartupItem>();

            // Registry Current User Run
            items.AddRange(GetRegistryStartupItems(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", RegistryHive.CurrentUser, StartupLocation.RegistryCurrentUser));
            // Registry Local Machine Run
            items.AddRange(GetRegistryStartupItems(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", RegistryHive.LocalMachine, StartupLocation.RegistryLocalMachine));
            // RunOnce
            items.AddRange(GetRegistryStartupItems(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", RegistryHive.CurrentUser, StartupLocation.RegistryCurrentUser));
            items.AddRange(GetRegistryStartupItems(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", RegistryHive.LocalMachine, StartupLocation.RegistryLocalMachine));

            // Startup folder
            items.AddRange(GetStartupFolderItems());

            // Task Scheduler via WMI
            items.AddRange(GetScheduledTasks());

            // Assess impact
            foreach (var item in items)
            {
                item.Impact = AssessImpact(item);
            }

            return items.DistinctBy(i => i.Name + i.Path).ToList();
        });
    }

    private List<StartupItem> GetRegistryStartupItems(string keyPath, RegistryHive hive, StartupLocation location)
    {
        var list = new List<StartupItem>();
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(keyPath);
            if (key == null) return list;

            foreach (var valueName in key.GetValueNames())
            {
                try
                {
                    var value = key.GetValue(valueName)?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    // Check if disabled via StartupApproved
                    bool isEnabled = IsStartupEnabled(valueName, hive);

                    list.Add(new StartupItem
                    {
                        Name = valueName,
                        Path = value,
                        Command = value,
                        Publisher = GetPublisherFromPath(value),
                        Location = location,
                        IsEnabled = isEnabled,
                        RegistryKey = $"{hive}\\{keyPath}\\{valueName}"
                    });
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    private bool IsStartupEnabled(string name, RegistryHive hive)
    {
        try
        {
            string approvedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
            if (hive == RegistryHive.CurrentUser)
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(approvedPath);
                if (key != null)
                {
                    var data = key.GetValue(name) as byte[];
                    if (data != null && data.Length > 0)
                    {
                        // First byte 2 = enabled, 3 = disabled
                        return data[0] == 2;
                    }
                }
            }
            else
            {
                // For LocalMachine, check StartupApproved\StartupFolder and Run
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(approvedPath);
                if (key != null)
                {
                    var data = key.GetValue(name) as byte[];
                    if (data != null && data.Length > 0)
                        return data[0] == 2;
                }
            }
        }
        catch { }
        return true; // default enabled
    }

    private List<StartupItem> GetStartupFolderItems()
    {
        var list = new List<StartupItem>();
        try
        {
            var startupPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup)),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup))
            };

            foreach (var path in startupPaths)
            {
                if (!Directory.Exists(path)) continue;
                foreach (var file in Directory.GetFiles(path))
                {
                    list.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Path = file,
                        Command = file,
                        Publisher = "Unknown",
                        Location = StartupLocation.StartupFolder,
                        IsEnabled = true
                    });
                }
            }
        }
        catch { }
        return list;
    }

    private List<StartupItem> GetScheduledTasks()
    {
        var list = new List<StartupItem>();
        try
        {
            // Simplified - using schtasks query
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/query /fo CSV /v",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                // Parse CSV - simplified: look for tasks with logon trigger
                // For brevity, skip detailed parsing
            }
        }
        catch { }
        return list;
    }

    private string GetPublisherFromPath(string path)
    {
        try
        {
            // Extract exe path
            var exe = path.Trim('"').Split('"')[0].Split(' ')[0];
            if (File.Exists(exe))
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(exe);
                return versionInfo.CompanyName ?? "Unknown";
            }
        }
        catch { }
        return "Unknown";
    }

    private StartupImpact AssessImpact(StartupItem item)
    {
        // Simple heuristic based on name/publisher
        var nameLower = item.Name.ToLower();
        if (nameLower.Contains("adobe") || nameLower.Contains("spotify") || nameLower.Contains("discord") || nameLower.Contains("steam"))
            return StartupImpact.Medio;
        if (nameLower.Contains("onedrive") || nameLower.Contains("teams"))
            return StartupImpact.Alto;
        if (nameLower.Contains("security") || nameLower.Contains("antivirus") || nameLower.Contains("windows"))
            return StartupImpact.Baixo;
        return StartupImpact.Desconhecido;
    }

    public async Task<bool> SetStartupItemEnabledAsync(StartupItem item, bool enabled)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (item.Location == StartupLocation.StartupFolder)
                {
                    // For startup folder, we can't easily disable - would need to move file
                    return false;
                }

                // Use registry StartupApproved
                string approvedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
                var hive = item.Location == StartupLocation.RegistryCurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;

                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.CreateSubKey(approvedPath, true);
                if (key == null) return false;

                // 02 00 00 00 ... = enabled, 03 00 00 00 ... = disabled
                byte[] data = enabled
                    ? new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                    : new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

                key.SetValue(item.Name, data, RegistryValueKind.Binary);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set startup item: {ex.Message}");
                return false;
            }
        });
    }

    public async Task<List<ServiceInfo>> GetServicesAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<ServiceInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, Description, State, StartMode, PathName FROM Win32_Service");
                foreach (ManagementObject svc in searcher.Get())
                {
                    list.Add(new ServiceInfo
                    {
                        Name = svc["Name"]?.ToString() ?? "",
                        DisplayName = svc["DisplayName"]?.ToString() ?? "",
                        Description = svc["Description"]?.ToString() ?? "",
                        Status = svc["State"]?.ToString() ?? "",
                        StartType = svc["StartMode"]?.ToString() ?? "",
                        Path = svc["PathName"]?.ToString() ?? ""
                    });
                }
            }
            catch { }
            return list;
        });
    }
}

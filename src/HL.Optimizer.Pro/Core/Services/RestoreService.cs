using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Diagnostics;
using System.Management;

namespace HL.Optimizer.Pro.Core.Services;

public class RestoreService : IRestoreService
{
    public async Task<bool> CreateRestorePointAsync(string description)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var mClass = new ManagementClass("\\\\.\\root\\default:SystemRestore");
                using var mMethod = mClass.GetMethodParameters("CreateRestorePoint");
                mMethod["Description"] = description;
                mMethod["RestorePointType"] = 12; // MODIFY_SETTINGS
                mMethod["EventType"] = 100; // BEGIN_SYSTEM_CHANGE

                using var result = mClass.InvokeMethod("CreateRestorePoint", mMethod, null);
                var returnValue = (uint?)result?["ReturnValue"] ?? 1;
                return returnValue == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Restore point creation failed: {ex.Message}");
                // Fallback via PowerShell
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType MODIFY_SETTINGS\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(30000);
                    return proc?.ExitCode == 0;
                }
                catch { return false; }
            }
        });
    }

    public async Task<List<RestorePointInfo>> GetRestorePointsAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<RestorePointInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\default", "SELECT * FROM SystemRestore");
                foreach (ManagementObject obj in searcher.Get())
                {
                    try
                    {
                        list.Add(new RestorePointInfo
                        {
                            SequenceNumber = Convert.ToInt32(obj["SequenceNumber"]),
                            Description = obj["Description"]?.ToString() ?? "",
                            CreationTime = ManagementDateTimeConverter.ToDateTime(obj["CreationTime"]?.ToString() ?? ""),
                            Type = obj["RestorePointType"]?.ToString() ?? ""
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Get restore points failed: {ex.Message}");
            }
            return list.OrderByDescending(r => r.CreationTime).ToList();
        });
    }

    public void OpenSystemRestore()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "rstrui.exe",
                UseShellExecute = true
            });
        }
        catch { }
    }

    public async Task BackupRegistryValueAsync(string keyPath, string valueName)
    {
        await Task.Run(() =>
        {
            try
            {
                var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HL Optimizer Pro", "Backups");
                Directory.CreateDirectory(backupDir);
                var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{keyPath.Replace("\\", "_")}_{valueName}.reg";
                var filePath = Path.Combine(backupDir, fileName);

                var psi = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"export \"{keyPath}\" \"{filePath}\" /y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch { }
        });
    }
}

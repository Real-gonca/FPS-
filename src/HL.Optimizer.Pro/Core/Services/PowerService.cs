using HL.Optimizer.Pro.Core.Interfaces;
using System.Diagnostics;

namespace HL.Optimizer.Pro.Core.Services;

public class PowerService : IPowerService
{
    public string HighPerformanceGuid => "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public string BalancedGuid => "381b4222-f694-41f0-9685-ff5bb260df2e";
    public string PowerSaverGuid => "a1841308-3541-4fab-bc81-f71556f20b4a";

    public async Task<List<string>> GetPowerPlansAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<string>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/list",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    list = output.Split('\n').Where(l => l.Contains("GUID")).ToList();
                }
            }
            catch { }
            return list;
        });
    }

    public async Task<string> GetActivePowerPlanAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    return output;
                }
            }
            catch { }
            return "";
        });
    }

    public async Task SetPowerPlanAsync(string planGuid)
    {
        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = $"/setactive {planGuid}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch { }
        });
    }

    public async Task SetHighPerformanceAsync() => await SetPowerPlanAsync(HighPerformanceGuid);
    public async Task SetBalancedAsync() => await SetPowerPlanAsync(BalancedGuid);
    public async Task SetPowerSavingAsync() => await SetPowerPlanAsync(PowerSaverGuid);
}

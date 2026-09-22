using System.Diagnostics;

namespace HL.Optimizer.Pro.Core.Utilities;

public static class PowerShellHelper
{
    public static async Task<(bool Success, string Output, string Error)> ExecuteAsync(string command, bool bypassExecutionPolicy = true)
    {
        try
        {
            var args = bypassExecutionPolicy
                ? $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "`\"")}\""
                : $"-NoProfile -Command \"{command.Replace("\"", "`\"")}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (false, "", "Failed to start PowerShell");
            var output = await proc.StandardOutput.ReadToEndAsync();
            var error = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    public static async Task<(bool Success, string Output)> ExecuteCmdAsync(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return (false, "");
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}

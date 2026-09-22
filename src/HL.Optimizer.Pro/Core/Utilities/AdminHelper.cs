using System.Diagnostics;
using System.Security.Principal;

namespace HL.Optimizer.Pro.Core.Utilities;

public static class AdminHelper
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static void EnsureManifest()
    {
        // Manifest is defined in project properties via app.manifest if needed
        // For development, we just check
    }

    public static bool RestartAsAdmin(string args = "")
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<(bool Success, string Output, string Error)> RunAsAdminAsync(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return (false, "", "Failed to start process");
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
}

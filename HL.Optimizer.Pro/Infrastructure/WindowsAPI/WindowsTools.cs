using System.Diagnostics;
namespace HL.Optimizer.Infrastructure.WindowsAPI;
public sealed record Tool(string Name, string Description, string Target, string Arguments = "");
public static class WindowsTools
{
    public static IReadOnlyList<Tool> Catalog { get; } = new Tool[]
    {
        new("Windows Update", "Open Windows Update settings", "ms-settings:windowsupdate"),
        new("Storage Sense", "Configure supported Windows storage cleanup", "ms-settings:storagesense"),
        new("Installed apps", "Review apps and uninstall using Windows", "ms-settings:appsfeatures"),
        new("Game Mode", "Review the Windows Game Mode preference", "ms-settings:gaming-gamemode"),
        new("Mouse", "Open supported pointer settings", "ms-settings:mousetouchpad"),
        new("Keyboard", "Open keyboard accessibility settings", "ms-settings:easeofaccess-keyboard"),
        new("Network", "Review adapter and DNS configuration in Windows", "ms-settings:network-status"),
        new("Power Options", "Review the power plan; higher performance may increase heat and power use", "control.exe", "/name Microsoft.PowerOptions"),
        new("Task Manager", "Inspect or manage processes using Windows protections", "taskmgr.exe"),
        new("Device Manager", "Review devices and drivers", "devmgmt.msc"),
        new("System Information", "Open Windows system information", "msinfo32.exe"),
        new("Event Viewer", "Review Windows event logs", "eventvwr.msc"),
        new("Disk Cleanup", "Run supported Windows cleanup", "cleanmgr.exe"),
        new("Windows Security", "Review security protections", "windowsdefender:"),
        new("System Restore", "Open the Windows restore wizard", "rstrui.exe"),
        new("System Protection", "Configure protection and create restore points manually", "SystemPropertiesProtection.exe")
    };
    public static void Launch(Tool tool)
    {
        if (!Catalog.Contains(tool)) throw new InvalidOperationException("Tool not allowlisted.");
        var target = tool.Target.Contains(':') ? tool.Target : Path.Combine(Environment.SystemDirectory, tool.Target);
        Process.Start(new ProcessStartInfo(target, tool.Arguments) { UseShellExecute = true });
    }
}

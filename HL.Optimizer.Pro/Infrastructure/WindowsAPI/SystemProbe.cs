using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using HL.Optimizer.Core.Interfaces;
using HL.Optimizer.Core.Models;
namespace HL.Optimizer.Infrastructure.WindowsAPI;
public sealed class SystemProbe : ISystemProbe
{
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, Load; public ulong Total, Available, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    private long previousIdle, previousTotal;
    private readonly object sampleLock = new();
    public Task<IReadOnlyList<Metric>> ReadAsync(CancellationToken token) => Task.Run<IReadOnlyList<Metric>>(() =>
    {
        token.ThrowIfCancellationRequested(); var list = new List<Metric>();
        lock (sampleLock)
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var total = kernel + user; var delta = total - previousTotal;
                double? usage = previousTotal == 0 || delta <= 0 ? null : Math.Clamp(100d * (delta - (idle - previousIdle)) / delta, 0, 100);
                previousTotal = total; previousIdle = idle;
                list.Add(new("CPU", usage.HasValue ? $"{usage:F0}%" : "N/D", $"{Environment.ProcessorCount} logical processors", usage));
            }
            else list.Add(new("CPU", "N/D", "Windows API unavailable"));
        }
        var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref mem)) list.Add(new("RAM", $"{(mem.Total - mem.Available) / 1073741824d:F1} GB", $"{mem.Total / 1073741824d:F1} GB installed · {mem.Load}% used", mem.Load));
        else list.Add(new("RAM", "N/D", "Memory API unavailable"));
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            list.Add(new("Storage", $"{drive.AvailableFreeSpace / 1073741824d:F1} GB free", drive.Name + " · system volume", 100d * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize));
        }
        catch (IOException e) { list.Add(new("Storage", "N/D", e.Message)); }
        list.Add(new("GPU", "N/D", "GPU sensor provider not yet integrated"));
        list.Add(new("Temperature", "N/D", "No verified sensor provider"));
        list.Add(new("Uptime", $"{TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours:F1} h", "Since last kernel boot"));
        list.Add(new("Windows", Environment.OSVersion.Version.ToString(), RuntimeInformation.OSArchitecture.ToString()));
        return list;
    }, token);
    public Task<IReadOnlyList<InventoryRow>> InventoryAsync(string category, CancellationToken token) => Task.Run<IReadOnlyList<InventoryRow>>(() =>
    {
        var rows = new List<InventoryRow>(); token.ThrowIfCancellationRequested();
        if (category == "Storage")
            foreach (var d in DriveInfo.GetDrives())
            {
                token.ThrowIfCancellationRequested();
                try { rows.Add(d.IsReady ? new(d.Name + " " + d.VolumeLabel, $"{d.AvailableFreeSpace / 1073741824d:F1} GB free / {d.TotalSize / 1073741824d:F1} GB", $"{d.DriveType} · {d.DriveFormat}") : new(d.Name, "Not ready", d.DriveType.ToString())); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { rows.Add(new(d.Name, "N/D", e.Message)); }
            }
        else if (category == "Network")
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                var p = n.GetIPProperties();
                rows.Add(new(n.Name, $"{n.OperationalStatus} · {n.Speed / 1000000} Mbps", $"IPv4: {string.Join(", ", p.UnicastAddresses.Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(a => a.Address))} | DNS: {string.Join(", ", p.DnsAddresses)} | Gateway: {string.Join(", ", p.GatewayAddresses.Select(a => a.Address))}"));
            }
        else if (category is "Process Manager" or "Gaming" or "Booster")
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var name = process.ProcessName;
                        if (category == "Gaming" && !new[] { "steam", "epic", "riot", "bluestacks", "hd-player", "bloodstrike", "freefire" }.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
                        rows.Add(new(name, $"PID {process.Id} · {process.WorkingSet64 / 1048576d:F1} MB", "Working set · read-only; no CPU/GPU estimate"));
                    }
                    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { rows.Add(new("Protected/exited process", "N/D", e.Message)); }
                }
            }
        else if (category == "Startup Manager")
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                    if (key is null) continue;
                    foreach (var name in key.GetValueNames()) rows.Add(new(name, key.GetValue(name)?.ToString() ?? "N/D", $"{hive} / {view} / Run · configured entry; enabled state not verified"));
                }
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
                if (Directory.Exists(dir)) foreach (var file in Directory.GetFiles(dir)) rows.Add(new(Path.GetFileName(file), file, "Startup folder"));
            rows.Add(new("Coverage", "Run keys and startup folders", "Scheduled tasks and services are not inventoried yet. Startup impact is not measured."));
        }
        else if (category is "Applications" or "Debloat")
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (key is null) continue;
                    foreach (var name in key.GetSubKeyNames())
                    {
                        token.ThrowIfCancellationRequested(); using var app = key.OpenSubKey(name);
                        if (app?.GetValue("DisplayName") is string title)
                            rows.Add(new(title, app.GetValue("DisplayVersion")?.ToString() ?? "N/D", app.GetValue("Publisher")?.ToString() ?? "Publisher not verified"));
                    }
                }
        return rows.OrderBy(x => x.Name).ToArray();
    }, token);
}

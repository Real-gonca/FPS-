using System.Management;
using System.Diagnostics;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using HL.Optimizer.Pro.Core.Utilities;

namespace HL.Optimizer.Pro.Core.Services;

public class SystemInfoService : ISystemInfoService
{
    public async Task<SystemInfo> GetSystemInfoAsync()
    {
        return await Task.Run(() =>
        {
            var info = new SystemInfo
            {
                ComputerName = Environment.MachineName,
                UserName = Environment.UserName,
                Architecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
                BootTime = GetBootTime(),
            };
            info.Uptime = DateTime.Now - info.BootTime;

            try
            {
                // OS
                var osQuery = WmiHelper.Query("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem");
                if (osQuery.Count > 0)
                {
                    var os = osQuery[0];
                    info.OsName = WmiHelper.GetPropertyString(os, "Caption", "Windows");
                    info.OsVersion = WmiHelper.GetPropertyString(os, "Version");
                    info.OsBuild = WmiHelper.GetPropertyString(os, "BuildNumber");
                    info.WindowsEdition = info.OsName;
                    info.Architecture = WmiHelper.GetPropertyString(os, "OSArchitecture", info.Architecture);
                }

                // CPU
                var cpuQuery = WmiHelper.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, Architecture FROM Win32_Processor");
                if (cpuQuery.Count > 0)
                {
                    var cpu = cpuQuery[0];
                    info.CpuName = WmiHelper.GetPropertyString(cpu, "Name").Trim();
                    info.CpuCores = WmiHelper.GetProperty<int>(cpu, "NumberOfCores", Environment.ProcessorCount);
                    info.CpuLogicalProcessors = WmiHelper.GetProperty<int>(cpu, "NumberOfLogicalProcessors", Environment.ProcessorCount);
                    var arch = WmiHelper.GetProperty<uint>(cpu, "Architecture", 0);
                    info.CpuArchitecture = arch switch { 0 => "x86", 1 => "MIPS", 2 => "Alpha", 3 => "PowerPC", 5 => "ARM", 6 => "ia64", 9 => "x64", 12 => "ARM64", _ => arch.ToString() };
                }

                // GPU
                var gpuQuery = WmiHelper.Query("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
                if (gpuQuery.Count > 0)
                {
                    var gpu = gpuQuery[0];
                    info.GpuName = WmiHelper.GetPropertyString(gpu, "Name");
                    info.GpuDriverVersion = WmiHelper.GetPropertyString(gpu, "DriverVersion");
                    var ram = WmiHelper.GetProperty<uint>(gpu, "AdapterRAM", 0);
                    info.GpuMemoryBytes = ram;
                }

                // RAM
                var memStatus = new NativeMethods.MEMORYSTATUSEX();
                memStatus.dwLength = (uint)Marshal.SizeOf(memStatus);
                if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
                {
                    info.TotalRamBytes = (long)memStatus.ullTotalPhys;
                    info.AvailableRamBytes = (long)memStatus.ullAvailPhys;
                }
                else
                {
                    var ramQuery = WmiHelper.Query("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                    if (ramQuery.Count > 0)
                    {
                        var ram = ramQuery[0];
                        var totalKb = WmiHelper.GetProperty<ulong>(ram, "TotalVisibleMemorySize", 0);
                        var freeKb = WmiHelper.GetProperty<ulong>(ram, "FreePhysicalMemory", 0);
                        info.TotalRamBytes = (long)(totalKb * 1024);
                        info.AvailableRamBytes = (long)(freeKb * 1024);
                    }
                }

                // Motherboard
                var mbQuery = WmiHelper.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                if (mbQuery.Count > 0)
                {
                    var mb = mbQuery[0];
                    var man = WmiHelper.GetPropertyString(mb, "Manufacturer");
                    var prod = WmiHelper.GetPropertyString(mb, "Product");
                    info.Motherboard = $"{man} {prod}".Trim();
                }

                // BIOS
                var biosQuery = WmiHelper.Query("SELECT Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS");
                if (biosQuery.Count > 0)
                {
                    var bios = biosQuery[0];
                    info.BiosManufacturer = WmiHelper.GetPropertyString(bios, "Manufacturer");
                    info.BiosVersion = WmiHelper.GetPropertyString(bios, "SMBIOSBIOSVersion");
                }

                // Disks
                info.Disks = GetDisksSync();

                // Network
                info.NetworkAdapters = GetNetworkAdaptersSync();

                // DirectX via registry
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
                    info.DirectXVersion = key?.GetValue("Version")?.ToString() ?? "DirectX 12";
                }
                catch { info.DirectXVersion = "DirectX 12"; }

                // Monitors
                try
                {
                    var monQuery = WmiHelper.Query("SELECT Name, ScreenWidth, ScreenHeight FROM Win32_DesktopMonitor");
                    foreach (var m in monQuery)
                    {
                        var w = WmiHelper.GetProperty<uint>(m, "ScreenWidth", 0);
                        var h = WmiHelper.GetProperty<uint>(m, "ScreenHeight", 0);
                        info.Monitors.Add(new MonitorInfo
                        {
                            Name = WmiHelper.GetPropertyString(m, "Name", "Monitor"),
                            Resolution = w > 0 && h > 0 ? $"{w}x{h}" : "1920x1080",
                            RefreshRate = 60
                        });
                    }
                    if (info.Monitors.Count == 0)
                    {
                        info.Monitors.Add(new MonitorInfo { Name = "Generic Monitor", Resolution = "1920x1080", RefreshRate = 60 });
                    }
                }
                catch { }

                foreach (var obj in osQuery) obj.Dispose();
                foreach (var obj in cpuQuery) obj.Dispose();
                foreach (var obj in gpuQuery) obj.Dispose();
                foreach (var obj in mbQuery) obj.Dispose();
                foreach (var obj in biosQuery) obj.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SystemInfo error: {ex.Message}");
            }

            return info;
        });
    }

    public async Task<List<DiskInfo>> GetDiskInfoAsync()
    {
        return await Task.Run(() => GetDisksSync());
    }

    private List<DiskInfo> GetDisksSync()
    {
        var list = new List<DiskInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                list.Add(new DiskInfo
                {
                    Name = drive.Name,
                    FileSystem = drive.DriveFormat,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace,
                    DriveType = drive.DriveType.ToString(),
                    Model = drive.Name
                });
            }

            // Enrich with WMI
            var diskQuery = WmiHelper.Query("SELECT Model, MediaType FROM Win32_DiskDrive");
            for (int i = 0; i < Math.Min(list.Count, diskQuery.Count); i++)
            {
                list[i].Model = WmiHelper.GetPropertyString(diskQuery[i], "Model", list[i].Model);
                list[i].MediaType = WmiHelper.GetPropertyString(diskQuery[i], "MediaType", list[i].MediaType);
            }
            foreach (var o in diskQuery) o.Dispose();
        }
        catch { }
        return list;
    }

    private List<NetworkAdapterInfo> GetNetworkAdaptersSync()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            var query = WmiHelper.Query("SELECT Name, Description, MACAddress, NetConnectionStatus FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True");
            foreach (var adapter in query)
            {
                var name = WmiHelper.GetPropertyString(adapter, "Name");
                var desc = WmiHelper.GetPropertyString(adapter, "Description");
                var mac = WmiHelper.GetPropertyString(adapter, "MACAddress");
                var status = WmiHelper.GetProperty<ushort>(adapter, "NetConnectionStatus", 0);
                list.Add(new NetworkAdapterInfo
                {
                    Name = name,
                    Description = desc,
                    MacAddress = mac,
                    IsConnected = status == 2
                });
            }
            foreach (var o in query) o.Dispose();
        }
        catch { }
        return list;
    }

    public async Task<string> GetWindowsVersionAsync()
    {
        var info = await GetSystemInfoAsync();
        return $"{info.OsName} {info.OsVersion} Build {info.OsBuild} {info.Architecture}";
    }

    private DateTime GetBootTime()
    {
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return DateTime.Now - uptime;
        }
        catch
        {
            return DateTime.Now.AddDays(-1);
        }
    }
}

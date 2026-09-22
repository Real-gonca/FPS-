namespace HL.Optimizer.Pro.Core.Models;

public class SystemInfo
{
    public string ComputerName { get; set; } = Environment.MachineName;
    public string UserName { get; set; } = Environment.UserName;
    public string OsName { get; set; } = "Windows";
    public string OsVersion { get; set; } = Environment.OSVersion.Version.ToString();
    public string OsBuild { get; set; } = "";
    public string Architecture { get; set; } = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
    public string WindowsEdition { get; set; } = "";
    public TimeSpan Uptime { get; set; }
    public DateTime BootTime { get; set; }
    public string CpuName { get; set; } = "";
    public int CpuCores { get; set; }
    public int CpuLogicalProcessors { get; set; }
    public string CpuArchitecture { get; set; } = "";
    public string GpuName { get; set; } = "";
    public string GpuDriverVersion { get; set; } = "";
    public long GpuMemoryBytes { get; set; }
    public long TotalRamBytes { get; set; }
    public long AvailableRamBytes { get; set; }
    public string Motherboard { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public string BiosManufacturer { get; set; } = "";
    public List<DiskInfo> Disks { get; set; } = new();
    public List<NetworkAdapterInfo> NetworkAdapters { get; set; } = new();
    public string DirectXVersion { get; set; } = "";
    public List<MonitorInfo> Monitors { get; set; } = new();
    public double CpuTemperature { get; set; }
    public double GpuTemperature { get; set; }
}

public class DiskInfo
{
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public double FreePercent => TotalBytes > 0 ? (double)FreeBytes / TotalBytes * 100 : 0;
    public string DriveType { get; set; } = "";
    public string MediaType { get; set; } = "";
}

public class NetworkAdapterInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string Dns { get; set; } = "";
    public string Speed { get; set; } = "";
    public bool IsConnected { get; set; }
}

public class MonitorInfo
{
    public string Name { get; set; } = "";
    public string Resolution { get; set; } = "";
    public int RefreshRate { get; set; }
}

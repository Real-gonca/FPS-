namespace HL.Optimizer.Pro.Core.Models;

public class PerformanceMetrics
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double CpuUsage { get; set; }
    public double RamUsage { get; set; }
    public long RamUsedBytes { get; set; }
    public long RamTotalBytes { get; set; }
    public long RamAvailableBytes { get; set; }
    public double GpuUsage { get; set; }
    public double GpuMemoryUsage { get; set; }
    public double DiskUsage { get; set; }
    public double DiskReadMBps { get; set; }
    public double DiskWriteMBps { get; set; }
    public double NetworkDownloadMbps { get; set; }
    public double NetworkUploadMbps { get; set; }
    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }
    public double CpuTemperature { get; set; }
    public double GpuTemperature { get; set; }
}

public class PerformanceHistory
{
    public List<PerformanceMetrics> Metrics { get; set; } = new();
    public int MaxPoints { get; set; } = 60;
    public void Add(PerformanceMetrics m)
    {
        Metrics.Add(m);
        if (Metrics.Count > MaxPoints) Metrics.RemoveAt(0);
    }
}

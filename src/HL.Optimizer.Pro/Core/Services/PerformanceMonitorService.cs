using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using HL.Optimizer.Pro.Core.Utilities;

namespace HL.Optimizer.Pro.Core.Services;

public class PerformanceMonitorService : IPerformanceMonitorService, IDisposable
{
    public event EventHandler<PerformanceMetrics>? MetricsUpdated;
    public PerformanceMetrics CurrentMetrics { get; private set; } = new();
    public PerformanceHistory History { get; } = new();

    private Timer? _timer;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramCounter;
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;
    private PerformanceCounter? _diskTimeCounter;
    private long _lastBytesReceived = 0;
    private long _lastBytesSent = 0;
    private DateTime _lastNetCheck = DateTime.Now;
    private bool _initialized = false;
    private readonly object _lock = new();

    public void StartMonitoring(int intervalMs = 1000)
    {
        if (_timer != null) StopMonitoring();
        InitializeCounters();
        _timer = new Timer(OnTimer, null, 0, intervalMs);
    }

    public void StopMonitoring()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void SetInterval(int intervalMs)
    {
        if (_timer != null)
        {
            _timer.Change(0, intervalMs);
        }
    }

    private void InitializeCounters()
    {
        if (_initialized) return;
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
            _diskTimeCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");

            // Warmup
            _cpuCounter.NextValue();
            _ramCounter.NextValue();
            _diskReadCounter.NextValue();
            _diskWriteCounter.NextValue();
            _diskTimeCounter.NextValue();

            Thread.Sleep(500);
            _initialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Performance counters init failed: {ex.Message}");
        }
    }

    private void OnTimer(object? state)
    {
        try
        {
            var metrics = new PerformanceMetrics
            {
                Timestamp = DateTime.Now
            };

            // CPU
            try
            {
                metrics.CpuUsage = _cpuCounter != null ? Math.Clamp(_cpuCounter.NextValue(), 0, 100) : GetCpuUsageFallback();
            }
            catch { metrics.CpuUsage = GetCpuUsageFallback(); }

            // RAM
            try
            {
                var memStatus = new NativeMethods.MEMORYSTATUSEX();
                memStatus.dwLength = (uint)Marshal.SizeOf(memStatus);
                if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
                {
                    metrics.RamTotalBytes = (long)memStatus.ullTotalPhys;
                    metrics.RamAvailableBytes = (long)memStatus.ullAvailPhys;
                    metrics.RamUsedBytes = metrics.RamTotalBytes - metrics.RamAvailableBytes;
                    metrics.RamUsage = metrics.RamTotalBytes > 0 ? (double)metrics.RamUsedBytes / metrics.RamTotalBytes * 100 : 0;
                }
            }
            catch { }

            // Disk
            try
            {
                metrics.DiskReadMBps = _diskReadCounter != null ? _diskReadCounter.NextValue() / (1024 * 1024) : 0;
                metrics.DiskWriteMBps = _diskWriteCounter != null ? _diskWriteCounter.NextValue() / (1024 * 1024) : 0;
                metrics.DiskUsage = _diskTimeCounter != null ? Math.Clamp(_diskTimeCounter.NextValue(), 0, 100) : 0;
            }
            catch { }

            // Network
            try
            {
                var now = DateTime.Now;
                var elapsed = (now - _lastNetCheck).TotalSeconds;
                if (elapsed >= 1)
                {
                    var netStats = GetNetworkBytes();
                    if (_lastBytesReceived > 0)
                    {
                        var downBytes = netStats.Received - _lastBytesReceived;
                        var upBytes = netStats.Sent - _lastBytesSent;
                        metrics.NetworkDownloadMbps = downBytes > 0 ? (downBytes * 8 / elapsed) / (1024 * 1024) : 0;
                        metrics.NetworkUploadMbps = upBytes > 0 ? (upBytes * 8 / elapsed) / (1024 * 1024) : 0;
                    }
                    _lastBytesReceived = netStats.Received;
                    _lastBytesSent = netStats.Sent;
                    _lastNetCheck = now;
                }
            }
            catch { }

            // Process counts
            try
            {
                var procs = Process.GetProcesses();
                metrics.ProcessCount = procs.Length;
                metrics.ThreadCount = procs.Sum(p => { try { return p.Threads.Count; } catch { return 0; } });
            }
            catch { }

            // GPU via WMI fallback - simplified
            try
            {
                metrics.GpuUsage = GetGpuUsage();
            }
            catch { }

            lock (_lock)
            {
                CurrentMetrics = metrics;
                History.Add(metrics);
            }

            MetricsUpdated?.Invoke(this, metrics);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Metrics timer error: {ex.Message}");
        }
    }

    private float GetCpuUsageFallback()
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
            Thread.Sleep(100);
            var endTime = DateTime.UtcNow;
            var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed) * 100;
            return (float)cpuUsageTotal;
        }
        catch { return 0; }
    }

    private double GetGpuUsage()
    {
        try
        {
            // Try performance counter for GPU Engine if available
            // Fallback to 0 if not available
            return 0;
        }
        catch { return 0; }
    }

    private (long Received, long Sent) GetNetworkBytes()
    {
        long received = 0, sent = 0;
        try
        {
            var query = WmiHelper.Query("SELECT BytesReceivedPersec, BytesSentPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");
            foreach (var obj in query)
            {
                received += WmiHelper.GetProperty<ulong>(obj, "BytesReceivedPersec", 0) > long.MaxValue ? 0 : (long)WmiHelper.GetProperty<ulong>(obj, "BytesReceivedPersec", 0);
                sent += WmiHelper.GetProperty<ulong>(obj, "BytesSentPersec", 0) > long.MaxValue ? 0 : (long)WmiHelper.GetProperty<ulong>(obj, "BytesSentPersec", 0);
                obj.Dispose();
            }
        }
        catch { }
        return (received, sent);
    }

    public void Dispose()
    {
        StopMonitoring();
        _cpuCounter?.Dispose();
        _ramCounter?.Dispose();
        _diskReadCounter?.Dispose();
        _diskWriteCounter?.Dispose();
        _diskTimeCounter?.Dispose();
    }
}

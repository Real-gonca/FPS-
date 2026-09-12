using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Metrics;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Telemetry;

/// <summary>
/// Composite de FONTES REAIS (spec §2.3) — cada fonte é independente:
/// uma falha não contamina as outras (degrada para N/D).
///
///   CPU                → PerformanceCounter
///   RAM livre          → PerformanceCounter (\Memory\Available MBytes)
///   RAM total          → GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
///   Disco              → DriveInfo("C:")
///   GPU/VRAM           → WMI Win32_VideoController
///   Temperatura        → WMI MSAcpi_ThermalZoneTemperature (throttle 15 s)
///   Uptime             → Environment.TickCount64
///   Processos          → Process.GetProcesses()
/// </summary>
public sealed class SystemTelemetryProvider : ISystemTelemetry
{
    private readonly PerformanceCounterSource _performanceCounters;
    private readonly WmiSystemInfo _wmi;
    private readonly ILogger<SystemTelemetryProvider> _log;

    public SystemTelemetryProvider(
        PerformanceCounterSource performanceCounters,
        WmiSystemInfo wmi,
        ILogger<SystemTelemetryProvider> log)
    {
        _performanceCounters = performanceCounters;
        _wmi = wmi;
        _log = log;
    }

    public async Task<SystemTelemetrySnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        double? cpu = await _performanceCounters.CpuUsagePercentAsync(ct);
        double? ramFreeMb = await _performanceCounters.AvailableMemoryMbAsync(ct);
        MetricValue ramTotalMb = ReadTotalRamMb();
        (MetricValue diskFreeGb, MetricValue diskTotalGb) = ReadPrimaryDisk();
        var (gpuName, vramTotalMb) = await _wmi.GetPrimaryGpuAsync(ct);
        double? temperatureC = await _wmi.GetTemperatureCachedAsync(ct);

        double? ramUsedMb = ramFreeMb is { } free && ramTotalMb.IsAvailable
            ? Math.Max(0.0, ramTotalMb.Value!.Value - free)
            : null;

        if (gpuName is not null)
            _log.LogDebug("GPU primária: {Name} (VRAM total {Vram} MB).", gpuName, vramTotalMb?.ToString("0") ?? "N/D");

        return new SystemTelemetrySnapshot(
            DateTimeOffset.UtcNow,
            MetricValue.Of(cpu),
            MetricValue.Of(ramUsedMb),
            MetricValue.Of(ramFreeMb),
            ramTotalMb,
            diskFreeGb,
            diskTotalGb,
            MetricValue.Of(vramTotalMb),
            MetricValue.Of(temperatureC),
            ReadUptime(),
            ReadProcessCount());
    }

    private static MetricValue ReadTotalRamMb()
    {
        try
        {
            long bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return bytes > 0 ? MetricValue.Of(bytes / (1024.0 * 1024.0)) : MetricValue.Missing();
        }
        catch
        {
            return MetricValue.Missing();
        }
    }

    private static (MetricValue Free, MetricValue Total) ReadPrimaryDisk()
    {
        try
        {
            string root = OperatingSystem.IsWindows() ? "C:" : "/";
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return (MetricValue.Missing(), MetricValue.Missing());

            const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;
            return (
                MetricValue.Of(drive.TotalFreeSpace / bytesPerGb),
                MetricValue.Of(drive.TotalSize / bytesPerGb));
        }
        catch
        {
            return (MetricValue.Missing(), MetricValue.Missing());
        }
    }

    private static TimeSpan? ReadUptime()
    {
        try
        {
            return TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadProcessCount()
    {
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                return processes.Length;
            }
            finally
            {
                foreach (var p in processes)
                    p.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }
}

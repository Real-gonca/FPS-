namespace Nexus.Domain.Metrics;

/// <summary>
/// Fotografia do sistema num instante, com fonte real identificável por campo:
/// CPU/RAM → PerformanceCounter + GC; disco → DriveInfo; GPU/VRAM → WMI
/// Win32_VideoController.AdapterRAM; temperatura → WMI root\WMI
/// MSAcpi_ThermalZoneTemperature (throttle 15 s); uptime → Environment.TickCount64;
/// processos → Process.GetProcesses.
/// </summary>
public sealed record SystemTelemetrySnapshot(
    DateTimeOffset TimestampUtc,
    MetricValue CpuUsagePercent,
    MetricValue RamUsedMb,
    MetricValue RamFreeMb,
    MetricValue RamTotalMb,
    MetricValue DiskFreeGb,
    MetricValue DiskTotalGb,
    MetricValue GpuVramTotalMb,
    MetricValue CpuTemperatureC,
    TimeSpan? Uptime,
    int? ActiveProcessCount)
{
    /// <summary>% de espaço livre no disco principal (N/D se qualquer parcela falhar).</summary>
    public MetricValue DiskFreePercent =>
        DiskFreeGb.IsAvailable && DiskTotalGb.IsAvailable && DiskTotalGb.Value > 0
            ? MetricValue.Of(Math.Clamp(DiskFreeGb.Value! / DiskTotalGb.Value!.Value * 100.0, 0, 100))
            : MetricValue.Missing();

    /// <summary>% de RAM livre (N/D se qualquer parcela falhar).</summary>
    public MetricValue RamFreePercent =>
        RamFreeMb.IsAvailable && RamTotalMb.IsAvailable && RamTotalMb.Value > 0
            ? MetricValue.Of(Math.Clamp(RamFreeMb.Value! / RamTotalMb.Value!.Value * 100.0, 0, 100))
            : MetricValue.Missing();

    /// <summary>% de RAM usada (N/D se qualquer parcela falhar).</summary>
    public MetricValue RamUsedPercent =>
        RamUsedMb.IsAvailable && RamTotalMb.IsAvailable && RamTotalMb.Value > 0
            ? MetricValue.Of(Math.Clamp(RamUsedMb.Value! / RamTotalMb.Value!.Value * 100.0, 0, 100))
            : MetricValue.Missing();
}

namespace Nexus.Domain.Ports;

/// <summary>
/// Amostra persistida (série temporal para "últimas N horas", spec §4.1).
/// Valores nulos = métrica estava N/D nesse instante (honestidade temporal).
/// </summary>
public sealed record TelemetrySample(
    DateTimeOffset TimestampUtc,
    double? CpuPercent,
    double? RamUsedPercent,
    double? TemperatureC,
    double? DiskFreePercent);

public interface ITelemetryHistory
{
    Task SaveSampleAsync(TelemetrySample sample, CancellationToken ct = default);

    /// <summary>Amostras das últimas <paramref name="hours"/> horas, em ordem cronológica.</summary>
    Task<IReadOnlyList<TelemetrySample>> GetRecentAsync(int hours, CancellationToken ct = default);
}

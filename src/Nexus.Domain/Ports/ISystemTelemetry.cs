using Nexus.Domain.Metrics;

namespace Nexus.Domain.Ports;

/// <summary>
/// Fonte real de telemetria do sistema (spec §2.3). Implementações:
/// PerformanceCounter (CPU/RAM), WMI (GPU/VRAM, temperatura), DriveInfo (disco),
/// Environment.TickCount64 (uptime), Process (processos).
/// Cada campo sem fonte disponível vem como N/D — nunca um valor inventado.
/// </summary>
public interface ISystemTelemetry
{
    Task<SystemTelemetrySnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

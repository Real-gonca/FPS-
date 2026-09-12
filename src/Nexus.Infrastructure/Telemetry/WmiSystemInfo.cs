using System.Management;
using Microsoft.Extensions.Logging;

namespace Nexus.Infrastructure.Telemetry;

/// <summary>
/// Fontes reais via WMI (spec §2.3):
/// - GPU/VRAM: Win32_VideoController (Name, AdapterRAM) — limitação conhecida:
///   AdapterRAM é 32 bits (VRAM total acima de 4 GB vem truncado; documentado);
/// - Temperatura CPU: root\WMI MSAcpi_ThermalZoneTemperature (décimos de grau),
///   THROTTLED a 15 s (WMI é lento — a spec exige 10–15 s).
///
/// Todas as consultas têm timeout de 2 s; qualquer falha → N/D (nunca crash).
/// </summary>
public sealed class WmiSystemInfo
{
    private static readonly TimeSpan WmiTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TemperatureThrottle = TimeSpan.FromSeconds(15);

    private readonly ILogger<WmiSystemInfo> _log;
    private readonly SemaphoreSlim _temperatureGate = new(1, 1);
    private double? _temperatureCache;
    private DateTimeOffset _temperatureFetchedUtc = DateTimeOffset.MinValue;

    public WmiSystemInfo(ILogger<WmiSystemInfo> log) => _log = log;

    /// <summary>Nome e VRAM total (MB) da GPU primária.</summary>
    public Task<(string? Name, double? VramTotalMb)> GetPrimaryGpuAsync(CancellationToken ct) =>
        Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows())
                return (null, (double?)null);

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT Name, AdapterRAM FROM Win32_VideoController",
                    new EnumerationOptions { Timeout = WmiTimeout });
                using var results = searcher.Get();

                string? name = null;
                double? vramMb = null;
                foreach (ManagementObject mo in results)
                {
                    name ??= mo["Name"]?.ToString();
                    if (mo["AdapterRAM"] is long bytes && bytes > 0 && vramMb is null)
                        vramMb = bytes / (1024.0 * 1024.0);
                }
                return (name, vramMb);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Consulta WMI de GPU falhou → N/D.");
                return (null, (double?)null);
            }
        }, ct);

    /// <summary>
    /// Temperatura da zona térmica (°C) com cache de 15 s (throttle da spec).
    /// Usa o valor mínimo das zonas &gt; 0 (a zona da CPU é tipicamente a mais
    /// baixa num portátil; em desktop corresponde à zona principal).
    /// </summary>
    public async Task<double?> GetTemperatureCachedAsync(CancellationToken ct)
    {
        await _temperatureGate.WaitAsync(ct);
        try
        {
            if (_temperatureCache is not null &&
                DateTimeOffset.UtcNow - _temperatureFetchedUtc < TemperatureThrottle)
            {
                return _temperatureCache;
            }

            _temperatureFetchedUtc = DateTimeOffset.UtcNow;
            _temperatureCache = await Task.Run(() => QueryTemperature(), ct);
            return _temperatureCache;
        }
        finally
        {
            _temperatureGate.Release();
        }
    }

    private double? QueryTemperature()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature",
                new EnumerationOptions { Timeout = WmiTimeout });
            using var results = searcher.Get();

            double? best = null;
            foreach (ManagementObject mo in results)
            {
                if (mo["CurrentTemperature"] is long tenthsOfDegree && tenthsOfDegree > 0)
                {
                    double celsius = tenthsOfDegree / 10.0;
                    if (best is null || celsius < best.Value)
                        best = celsius;
                }
            }
            return best;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Consulta WMI de temperatura falhou → N/D.");
            return null;
        }
    }
}

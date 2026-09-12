using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Nexus.Infrastructure.Telemetry;

/// <summary>
/// Fonte real: PerformanceCounter (spec §2.3).
/// - CPU: \Processor Information(_Total)\% Processor Time
/// - RAM livre: \Memory\Available MBytes
///
/// Os contadores ficam vivos entre amostras (padrão standard: NextValue()
/// por ciclo). Fora do Windows (ou sem permissão) → null → a UI mostra N/D.
/// </summary>
public sealed class PerformanceCounterSource
{
    private readonly object _gate = new();
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramCounter;
    private bool _warned;

    private readonly ILogger<PerformanceCounterSource> _log;

    public PerformanceCounterSource(ILogger<PerformanceCounterSource> log) => _log = log;

    public Task<double?> CpuUsagePercentAsync(CancellationToken ct) => Safe(
        () =>
        {
            lock (_gate)
            {
                try
                {
                    _cpuCounter ??= new PerformanceCounter("Processor Information", "% Processor Time", "_Total");
                    double value = _cpuCounter.NextValue();
                    return value is >= 0 and <= 100 ? value : null;
                }
                catch (Exception ex)
                {
                    WarnOnce(ex);
                    return null;
                }
            }
        }, ct);

    public Task<double?> AvailableMemoryMbAsync(CancellationToken ct) => Safe(
        () =>
        {
            lock (_gate)
            {
                try
                {
                    _ramCounter ??= new PerformanceCounter("Memory", "Available MBytes");
                    double mb = _ramCounter.NextValue();
                    return mb >= 0 ? mb : null;
                }
                catch (Exception ex)
                {
                    WarnOnce(ex);
                    return null;
                }
            }
        }, ct);

    private Task<double?> Safe(Func<double?> work, CancellationToken ct) =>
        OperatingSystem.IsWindows()
            ? Task.Run(work, ct)
            : Task.FromResult<double?>(null); // não-Windows → N/D honesto

    private void WarnOnce(Exception ex)
    {
        if (_warned)
            return;
        _warned = true;
        _log.LogWarning(ex, "PerformanceCounter indisponível neste sistema → métricas CPU/RAM ficarão N/D.");
    }
}

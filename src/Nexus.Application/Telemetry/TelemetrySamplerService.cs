using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.Application.Scoring;
using Nexus.Domain.Metrics;
using Nexus.Domain.Ports;

namespace Nexus.Application.Telemetry;

/// <summary>
/// Amostra métricas reais a cada 5 s (spec §2.3), persiste em SQLite
/// (série temporal para o gráfico "últimas N horas") e publica em
/// <see cref="TelemetryEvents"/>.
///
/// Resiliência (spec §5): qualquer falha de WMI/PerformanceCounter é
/// capturada e logada — a app nunca crasha; as métricas afetadas ficam N/D.
/// </summary>
public sealed class TelemetrySamplerService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly ISystemTelemetry _telemetry;
    private readonly ITelemetryHistory _history;
    private readonly ScoreEngine _score;
    private readonly TelemetryEvents _events;
    private readonly ILogger<TelemetrySamplerService> _log;

    public TelemetrySamplerService(
        ISystemTelemetry telemetry,
        ITelemetryHistory history,
        ScoreEngine score,
        TelemetryEvents events,
        ILogger<TelemetrySamplerService> log)
    {
        _telemetry = telemetry;
        _history = history;
        _score = score;
        _events = events;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Amostragem de telemetria iniciada (intervalo {Interval}s).", Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _telemetry.GetSnapshotAsync(stoppingToken);
                var score = _score.ComputeScore(snapshot);

                await _history.SaveSampleAsync(new TelemetrySample(
                    snapshot.TimestampUtc,
                    snapshot.CpuUsagePercent.Value,
                    snapshot.RamUsedPercent.Value,
                    snapshot.CpuTemperatureC.Value,
                    snapshot.DiskFreePercent.Value), stoppingToken);

                _events.RaiseSample(snapshot, score);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Amostra falhou — as métricas afetadas apresentar-se-ão como N/D.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _log.LogInformation("Amostragem de telemetria terminada.");
    }
}

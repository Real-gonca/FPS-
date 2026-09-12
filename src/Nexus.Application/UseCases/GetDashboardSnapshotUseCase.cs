using Microsoft.Extensions.Logging;
using Nexus.Application.Recommendations;
using Nexus.Application.Scoring;
using Nexus.Domain.Metrics;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application.UseCases;

public sealed record DashboardPayload(
    SystemTelemetrySnapshot Snapshot,
    ScoreResult Score,
    IReadOnlyList<Recommendation> Recommendations,
    StorageProbeResult? TempProbe);

/// <summary>
/// Caso de uso: dados do Dashboard (spec §4.1) — snapshot real + score +
/// recomendações. O varrimento de temporários é time-boxed (5 s); em timeout
/// o valor é etiquetado "aprox." na UI (spec §2.3).
/// </summary>
public sealed class GetDashboardSnapshotUseCase
{
    private static readonly TimeSpan ProbeTimeBox = TimeSpan.FromSeconds(5);

    private readonly ISystemTelemetry _telemetry;
    private readonly ScoreEngine _score;
    private readonly RecommendationsEngine _recommendations;
    private readonly IStorageProbe _storage;
    private readonly ISettingsService _settings;
    private readonly ILogger<GetDashboardSnapshotUseCase> _log;

    public GetDashboardSnapshotUseCase(
        ISystemTelemetry telemetry,
        ScoreEngine score,
        RecommendationsEngine recommendations,
        IStorageProbe storage,
        ISettingsService settings,
        ILogger<GetDashboardSnapshotUseCase> log)
    {
        _telemetry = telemetry;
        _score = score;
        _recommendations = recommendations;
        _storage = storage;
        _settings = settings;
        _log = log;
    }

    public async Task<DashboardPayload> ExecuteAsync(CancellationToken ct = default)
    {
        var snapshot = await _telemetry.GetSnapshotAsync(ct);
        var score = _score.ComputeScore(snapshot);

        StorageProbeResult? tempProbe = null;
        string? tempPath = ResolveTempPath();
        if (tempPath is not null && Directory.Exists(tempPath))
        {
            try
            {
                tempProbe = await _storage.MeasureAsync(tempPath, ProbeTimeBox, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Probe de temporários falhou — a recomendação de limpeza não será exibida.");
                tempProbe = null;
            }
        }

        var settings = await _settings.LoadAsync(ct);
        var recommendations = _recommendations.Build(snapshot, tempProbe, settings.AdvancedMode);

        return new DashboardPayload(snapshot, score, recommendations, tempProbe);
    }

    private static string? ResolveTempPath()
    {
        var temp = Environment.GetEnvironmentVariable("TEMP");
        if (!string.IsNullOrWhiteSpace(temp))
            return temp;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return local.Length == 0 ? null : Path.Combine(local, "Temp");
    }
}

using Nexus.Application.Scoring;
using Nexus.Domain.Metrics;

namespace Nexus.Application.Telemetry;

/// <summary>
/// Canal de telemetria ao vivo: o sampler publica aqui a cada ciclo e a
/// Presentation assina (fazendo o marshal para o thread da UI).
/// </summary>
public sealed class TelemetryEvents
{
    public sealed record SampleEvent(SystemTelemetrySnapshot Snapshot, ScoreResult Score);

    public event EventHandler<SampleEvent>? SampleRecorded;

    public void RaiseSample(SystemTelemetrySnapshot snapshot, ScoreResult score) =>
        SampleRecorded?.Invoke(this, new SampleEvent(snapshot, score));
}

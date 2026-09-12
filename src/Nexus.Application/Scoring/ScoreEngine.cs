using Nexus.Domain.Metrics;

namespace Nexus.Application.Scoring;

/// <summary>Fator individual do score (exposto para transparência/relatórios).</summary>
public sealed record ScoreFactor(string Name, double Weight, double? FactorValue, bool Available, string Rationale);

public sealed record ScoreResult(double? Score, IReadOnlyList<ScoreFactor> Factors, string Summary);

/// <summary>
/// Calcula o Score de Desempenho (0–100) a partir de métricas REAIS, com
/// ponderação transparente (spec §3.2 / §4.1):
///
///   Espaço em disco livre   30 pts   (fator = % livre no disco principal)
///   Memória RAM livre       30 pts   (fator = % livre)
///   Pressão de CPU          20 pts   (fator = 100 − uso)
///   Temperatura             20 pts   (fator = 100 a ≤55 °C → 0 a ≥90 °C)
///
/// Regras de honestidade:
/// - métrica indisponível (N/D) → o peso é redistribuído aos fatores
///   disponíveis (reponderação proporcional — documentada no Summary);
/// - todas indisponíveis → Score = null (a UI mostra N/D, nunca inventa).
/// </summary>
public sealed class ScoreEngine
{
    public const double DiskWeight = 30;
    public const double RamWeight = 30;
    public const double CpuWeight = 20;
    public const double TemperatureWeight = 20;

    public ScoreResult ComputeScore(SystemTelemetrySnapshot s)
    {
        var factors = new List<ScoreFactor>(4);
        double weightSum = 0;
        double accumulated = 0;

        void Add(string name, double weight, double? value, string rationale)
        {
            bool available = value is not null;
            factors.Add(new ScoreFactor(name, weight, value, available, rationale));
            if (available)
            {
                weightSum += weight;
                accumulated += weight * value!.Value;
            }
        }

        Add("Espaço em disco", DiskWeight,
            PercentOf(s.DiskFreeGb, s.DiskTotalGb),
            "Percentagem de espaço livre no disco principal.");

        Add("Memória livre", RamWeight,
            PercentOf(s.RamFreeMb, s.RamTotalMb),
            "Percentagem de RAM livre.");

        Add("Pressão de CPU", CpuWeight,
            s.CpuUsagePercent.IsAvailable
                ? Math.Clamp(100.0 - s.CpuUsagePercent.Value!.Value, 0, 100)
                : null,
            "100 − uso de CPU (CPU ociosa → fator melhor).");

        Add("Temperatura", TemperatureWeight,
            s.CpuTemperatureC.IsAvailable
                ? Math.Clamp((90.0 - s.CpuTemperatureC.Value!.Value) / 35.0 * 100.0, 0, 100)
                : null,
            "≤55 °C = ótimo (100); ≥90 °C = crítico (0). Fonte: zona térmica ACPI (throttle 15 s).");

        double? score = weightSum > 0 ? Math.Round(accumulated / weightSum) : null;
        return new ScoreResult(score, factors, BuildSummary(score, factors));
    }

    private static double? PercentOf(MetricValue part, MetricValue total) =>
        part.IsAvailable && total.IsAvailable && total.Value is > 0
            ? Math.Clamp(part.Value! / total.Value!.Value * 100.0, 0, 100)
            : null;

    private static string BuildSummary(double? score, IReadOnlyList<ScoreFactor> factors)
    {
        if (score is null)
            return "Sem métricas reais disponíveis — score apresentado como N/D.";

        int available = factors.Count(f => f.Available);
        int missing = factors.Count - available;
        string missingPart = missing switch
        {
            0 => string.Empty,
            1 => " (1 fator indisponível → pesos reponderados)",
            _ => $" ({missing} fatores indisponíveis → pesos reponderados)",
        };
        return $"Baseado em {available} de {factors.Count} fatores reais.{missingPart}";
    }
}

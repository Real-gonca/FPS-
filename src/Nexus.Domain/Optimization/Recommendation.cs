namespace Nexus.Domain.Optimization;

/// <summary>
/// Recomendação gerada pelo RecommendationsEngine a partir de dados reais.
/// O impacto é uma estimativa transparente (regra documentada), nunca uma
/// medição — a medição A/B chega com o benchmark (spec §4.2).
/// </summary>
public sealed record Recommendation(
    string TaskKey,
    string Title,
    string Description,
    int ImpactPercent,
    RiskLevel Risk,
    bool Reversible,
    /// <summary>
    /// true = recomendação informativa (ex.: temperatura elevada) —
    /// não corresponde a uma tarefa aplicável.
    /// </summary>
    bool IsAdvisory,
    FeatureVisibility Visibility);

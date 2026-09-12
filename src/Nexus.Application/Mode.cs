using Nexus.Domain.Optimization;

namespace Nexus.Application;

/// <summary>
/// Filtro de Modo Simples/Avançado (spec §7): no Modo Simples fica oculto
/// TUDO o que está marcado como <see cref="FeatureVisibility.Advanced"/> —
/// navegação, recomendações e (futuramente) pesquisa.
/// </summary>
public static class ModeFilter
{
    public static bool IsVisible(FeatureVisibility visibility, bool advancedMode) =>
        advancedMode || visibility == FeatureVisibility.Simple;
}

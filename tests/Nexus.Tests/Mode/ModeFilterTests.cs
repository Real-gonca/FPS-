using Nexus.Application;
using Nexus.Domain.Optimization;
using Xunit;

namespace Nexus.Tests.Mode;

/// <summary>
/// Spec §7: "Modo Simples esconde corretamente tudo o que é marcado como
/// Avançado" — a regra central de filtragem (navegação, recomendações, pesquisa).
/// </summary>
public class ModeFilterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimpleVisibility_IsAlwaysVisible(bool advancedMode)
    {
        Assert.True(ModeFilter.IsVisible(FeatureVisibility.Simple, advancedMode));
    }

    [Fact]
    public void AdvancedVisibility_IsHiddenInSimpleMode()
    {
        Assert.False(ModeFilter.IsVisible(FeatureVisibility.Advanced, advancedMode: false));
    }

    [Fact]
    public void AdvancedVisibility_IsVisibleInAdvancedMode()
    {
        Assert.True(ModeFilter.IsVisible(FeatureVisibility.Advanced, advancedMode: true));
    }
}

using Nexus.Application.Profiles;
using Xunit;

namespace Nexus.Tests.Profiles;

/// <summary>
/// Perfis (spec §4.6): só serviços Windows reais (nomes curtos válidos para
/// a whitelist do sc.exe), start modes válidos, justificação obrigatória em
/// cada alteração, todos reversíveis.
/// </summary>
public class ServiceProfileTests
{
    [Fact]
    public void GamingAndPrivacy_ProfilesExist()
    {
        Assert.NotNull(ServiceProfileCatalog.Find("gaming"));
        Assert.NotNull(ServiceProfileCatalog.Find("privacy"));
    }

    [Theory]
    [InlineData("gaming")]
    [InlineData("privacy")]
    public void EveryChange_HasRealServiceName_ValidMode_AndJustification(string key)
    {
        var profile = ServiceProfileCatalog.Find(key)!;

        Assert.NotEmpty(profile.Changes);
        foreach (var change in profile.Changes)
        {
            // Nome curto válido para o padrão da whitelist do sc.exe.
            Assert.Matches("^[A-Za-z0-9_.\\-]{1,128}$", change.ServiceKey);
            Assert.False(string.IsNullOrWhiteSpace(change.DisplayName));
            Assert.Contains(change.TargetStartMode, new[] { "auto", "demand", "disabled" });
            Assert.True(change.Justification.Length >= 20,
                $"Sem justificação útil para {change.ServiceKey} no perfil {key}.");
        }
    }

    [Theory]
    [InlineData("gaming")]
    [InlineData("privacy")]
    public void Profiles_AreReversible_AndRequireElevation(string key)
    {
        var profile = ServiceProfileCatalog.Find(key)!;

        Assert.True(profile.Reversible);
        Assert.True(profile.RequiresElevation);
        Assert.Equal(Nexus.Domain.Optimization.RiskLevel.Low, profile.Risk);
    }

    [Fact]
    public void Privacy_Profile_IncludesDiagTrack()
    {
        var profile = ServiceProfileCatalog.Find("privacy")!;

        Assert.Contains(profile.Changes, c => c.ServiceKey == "DiagTrack" && c.TargetStartMode == "disabled");
    }
}

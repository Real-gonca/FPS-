using Nexus.Domain.Optimization;

namespace Nexus.Application.Profiles;

/// <summary>
/// Uma alteração individual de um perfil (spec §4.6: "perfis de otimização
/// (Gaming, Privacidade) com preview de impacto antes de aplicar").
/// Justification OBRIGATÓRIA — cada mudança de serviço é documentada.
/// </summary>
public sealed record ServiceProfileChange(
    string ServiceKey,
    string DisplayName,
    string TargetStartMode,
    string Justification);

/// <summary>
/// Perfil de serviços = conjunto de alterações documentadas, aplicável um a
/// um (cada alteração é atómica, com backup + rollback individual — o
/// perfil NUNCA aplica "meia-meia": se uma falhar, as restantes não correm
/// e as já aplicadas podem ser revertidas individualmente).
/// </summary>
public sealed class ServiceProfile
{
    public ServiceProfile(
        string key,
        string name,
        string description,
        RiskLevel risk,
        IReadOnlyList<ServiceProfileChange> changes,
        string? documentationUrl = null)
    {
        Key = key;
        Name = name;
        Description = description;
        Risk = risk;
        Changes = changes;
        DocumentationUrl = documentationUrl;
    }

    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public IReadOnlyList<ServiceProfileChange> Changes { get; }
    public string? DocumentationUrl { get; }

    public bool Reversible => true;

    public bool RequiresElevation => true;

    public string KeyFor(string serviceKey) => $"{Key}-{serviceKey.ToLowerInvariant()}";
}

using Nexus.Application.Recommendations;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application.Tasks;

/// <summary>
/// Desativa o ID de anúncio (Advertising ID) — spec §4.7.
///
/// Documentação (spec §4.7 — cada toggle documenta o que faz e porquê é seguro):
/// - o que faz: impede as apps do Windows/loja de usar o ID de anúncio para
///   publicidade personalizada (HKCU\...\AdvertisingInfo\Enabled=0);
/// - porquê é seguro: afeta apenas publicidade; não afeta privacidade de rede,
///   atualizações nem a funcionalidade das apps;
/// - porquê NÃO precisa de admin: é HKCU (conta do utilizador atual);
/// - como reverte: rollback restaura o valor anterior (ou a ausência);
/// - referência: https://learn.microsoft.com/windows/privacy/
/// </summary>
public sealed class AdvertisingIdTask : IOptimizationTask
{
    private const string AdInfoPath = "HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo";
    private const string ValueName = "Enabled";

    private readonly IRegistryAccess _registry;

    public AdvertisingIdTask(IRegistryAccess registry) => _registry = registry;

    public string Key => TaskKeys.AdvertisingIdDisable;

    public string Name => "Desativar ID de anúncio (publicidade personalizada)";

    public string Description =>
        "Define Enabled=0 em HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo. " +
        "As apps deixam de usar o ID de anúncio para publicidade personalizada. Não afeta atualizações " +
        "nem a funcionalidade das apps. Não requer administrador (HKCU). Reversível por rollback.";

    public string? DocumentationUrl => "https://learn.microsoft.com/windows/privacy/";

    public RiskLevel Risk => RiskLevel.Low;

    public bool Reversible => true;

    public bool RequiresElevation => false;

    public FeatureVisibility Visibility => FeatureVisibility.Simple;

    public TimeSpan EstimatedDuration => TimeSpan.FromSeconds(2);

    public IReadOnlyList<ChangeDescriptor> DescribeChanges() => new[]
    {
        new ChangeDescriptor(
            ChangeKind.Registry,
            AdInfoPath,
            ValueName,
            "Valor DWORD Enabled do AdvertisingInfo — o estado anterior (valor ou ausência) fica guardado no backup."),
    };

    public Task ApplyAsync(CancellationToken ct = default) =>
        _registry.SetAsync(AdInfoPath, ValueName, RegistryValueKind.Dword, 0, ct);
}

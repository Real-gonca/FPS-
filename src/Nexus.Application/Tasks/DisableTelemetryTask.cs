using Nexus.Application.Recommendations;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application.Tasks;

/// <summary>
/// PRIMEIRA TAREFA REAL (Patch 1): desativa a telemetria de base do Windows
/// via chave de política AllowTelemetry=0 (HKLM).
///
/// Documentação (spec §4.7 — cada tweak documenta o que faz e porquê é seguro):
/// - o que faz: impede a recolha de dados de diagnóstico/CEIP pelo Windows;
/// - porquê é seguro: não desativa o Windows Update, a segurança, nem o
///   diagnóstico de rede; é a mesma chave usada pelas GPO "Allow Telemetry";
/// - como reverte: rollback restaura o valor anterior (ou a ausência do valor);
/// - referência: https://learn.microsoft.com/windows/privacy/privacy-windows
/// </summary>
public sealed class DisableTelemetryTask : IOptimizationTask
{
    private const string PolicyPath = "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection";
    private const string ValueName = "AllowTelemetry";

    private readonly IRegistryAccess _registry;

    public DisableTelemetryTask(IRegistryAccess registry) => _registry = registry;

    public string Key => TaskKeys.TelemetryDisable;

    public string Name => "Desativar telemetria de base (Windows)";

    public string Description =>
        "Define AllowTelemetry=0 em HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection. " +
        "Reduz a recolha de dados de diagnóstico pelo Windows. Não afeta o Windows Update, a segurança " +
        "nem o diagnóstico de rede. Reversível por rollback (o valor anterior é guardado).";

    public string? DocumentationUrl => "https://learn.microsoft.com/windows/privacy/privacy-windows";

    public RiskLevel Risk => RiskLevel.Low;

    public bool Reversible => true;

    public bool RequiresElevation => true;

    public FeatureVisibility Visibility => FeatureVisibility.Simple;

    public TimeSpan EstimatedDuration => TimeSpan.FromSeconds(3);

    public IReadOnlyList<ChangeDescriptor> DescribeChanges() =>
        new[]
        {
            new ChangeDescriptor(
                ChangeKind.Registry,
                PolicyPath,
                ValueName,
                "Valor DWORD AllowTelemetry — o estado anterior (valor ou ausência) fica guardado no backup."),
        };

    public Task ApplyAsync(CancellationToken ct = default) =>
        _registry.SetAsync(PolicyPath, ValueName, RegistryValueKind.Dword, 0, ct);
}

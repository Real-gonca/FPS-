using Nexus.Application.Recommendations;

namespace Nexus.Application.Optimization;

/// <summary>
/// O conjunto "Otimização Rápida" (spec §4.2): tarefas SEGURAS (baixo risco,
/// reversíveis, documentadas) aplicáveis num clique, com micro-benchmark
/// medido antes/depois (números reais) e rollback individual por tarefa.
///
/// Cada entrada aponta para uma tarefa registada no catálogo — a UI só
/// mostra itens cuja tarefa existe (CanApply), mantendo a promessa honesta.
/// </summary>
public static class QuickOptimizationSet
{
    public sealed record QuickItem(
        string TaskKey,
        string Title,
        string Description,
        Domain.Optimization.RiskLevel Risk);

    public static IReadOnlyList<QuickItem> Items => new List<QuickItem>
    {
        new(
            TaskKeys.TelemetryDisable,
            "Desativar telemetria de base",
            "Política AllowTelemetry=0 (HKLM). Reduz a recolha de dados de diagnóstico do Windows. Reversível.",
            Domain.Optimization.RiskLevel.Low),

        new(
            TaskKeys.ServiceDiagTrackDisable,
            "Desativar serviço de telemetria (DiagTrack)",
            "Põe o serviço 'Connected User Experiences and Telemetry' em disabled. Reversível por rollback.",
            Domain.Optimization.RiskLevel.Low),

        new(
            TaskKeys.ServiceWmpNetworkDisable,
            "Desativar partilha de media (WMPNetworkSvc)",
            "Põe a partilha de rede do Media Player em disabled — sem ciclos em segundo plano. Reversível.",
            Domain.Optimization.RiskLevel.Low),

        new(
            TaskKeys.AdvertisingIdDisable,
            "Desativar ID de anúncio",
            "AdvertisingInfo\Enabled=0 (HKCU) — publicidade personalizada desligada. Não requer admin. Reversível.",
            Domain.Optimization.RiskLevel.Low),
    };
}

using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Gerenciador de planos de energia (powercfg + powrprof P/Invoke).</summary>
public interface IPowerPlanService
{
    /// <summary>Lista os planos disponíveis.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Obtém o plano ativo.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PowerPlanInfo?> GetActivePlanAsync(CancellationToken cancellationToken = default);

    /// <summary>Ativa um plano pelo GUID.</summary>
    /// <param name="planGuid">GUID do esquema.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> ActivateAsync(string planGuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Garante a existência do plano "Desempenho Máximo" (duplica o plano de alto
    /// desempenho quando o GUID Ultimate não estiver registrado) e o ativa.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PowerPlanInfo?> EnableUltimatePerformanceAsync(CancellationToken cancellationToken = default);

    /// <summary>Restaura um plano anterior (usado ao desativar o Modo Gamer).</summary>
    /// <param name="planGuid">GUID a restaurar (null = Equilibrado).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> RestorePlanAsync(string? planGuid, CancellationToken cancellationToken = default);

    /// <summary>Desativa a suspensão seletiva USB e o throttling de processador.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> ApplyGamerPowerTweaksAsync(CancellationToken cancellationToken = default);

    /// <summary>Reverte os tweaks de energia aplicados por <see cref="ApplyGamerPowerTweaksAsync"/>.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> RevertGamerPowerTweaksAsync(CancellationToken cancellationToken = default);
}

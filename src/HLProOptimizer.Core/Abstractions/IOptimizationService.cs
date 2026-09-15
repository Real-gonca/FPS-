using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Orquestrador da tela "Otimização".</summary>
public interface IOptimizationService
{
    /// <summary>Todos os passos registrados no sistema.</summary>
    IReadOnlyList<IOptimizationStep> AvailableSteps { get; }

    /// <summary>Passos que seriam executados para um conjunto de opções.</summary>
    /// <param name="options">Opções selecionadas na UI.</param>
    IReadOnlyList<IOptimizationStep> GetPlan(OptimizationOptions options);

    /// <summary>Executa a otimização conforme as opções, com log e progresso em tempo real.</summary>
    /// <param name="options">Opções (modo + toggles + nível).</param>
    /// <param name="progress">Progresso por passo.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<OptimizationResult> OptimizeAsync(
        OptimizationOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reverte os passos reversíveis de um modo (Restaurar Padrões).</summary>
    /// <param name="mode">Modo a reverter.</param>
    /// <param name="progress">Progresso.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<OptimizationResult> RevertAsync(
        OptimizationMode mode,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

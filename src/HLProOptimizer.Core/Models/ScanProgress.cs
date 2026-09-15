using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>
/// Progresso incremental de uma operação longa (scan, limpeza, otimização).
/// Reportado via <see cref="IProgress{T}"/> para atualizar a barra da UI.
/// </summary>
/// <param name="CurrentCategory">Categoria/etapa em execução.</param>
/// <param name="CurrentStepName">Nome legível do passo atual.</param>
/// <param name="CompletedSteps">Passos concluídos.</param>
/// <param name="TotalSteps">Total de passos planejados.</param>
/// <param name="Message">Mensagem para o log em tempo real.</param>
public sealed record ScanProgress(
    IssueCategory CurrentCategory,
    string CurrentStepName,
    int CompletedSteps,
    int TotalSteps,
    string Message)
{
    /// <summary>Percentual concluído [0-100].</summary>
    public double Percent => TotalSteps <= 0 ? 0d : Math.Clamp((CompletedSteps / (double)TotalSteps) * 100d, 0d, 100d);
}

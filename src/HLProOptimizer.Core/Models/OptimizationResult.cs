using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>Resumo final de uma otimização, exibido no diálogo de conclusão.</summary>
public sealed class OptimizationResult
{
    /// <summary>Modo executado.</summary>
    public required OptimizationMode Mode { get; init; }

    /// <summary>Nível de agressividade aplicado.</summary>
    public OptimizationLevel Level { get; init; } = OptimizationLevel.Balanced;

    /// <summary>Início.</summary>
    public DateTime StartedAt { get; init; } = DateTime.Now;

    /// <summary>Conclusão.</summary>
    public DateTime CompletedAt { get; init; } = DateTime.Now;

    /// <summary>Resultado de cada passo.</summary>
    public IReadOnlyList<OptimizationStepResult> Steps { get; init; } = [];

    /// <summary>Passos bem-sucedidos.</summary>
    public int SuccessCount => Steps.Count(s => s.Success && !s.Skipped);

    /// <summary>Passos ignorados.</summary>
    public int SkippedCount => Steps.Count(s => s.Skipped);

    /// <summary>Passos com falha.</summary>
    public int FailedCount => Steps.Count(s => !s.Success);

    /// <summary>Espaço total liberado.</summary>
    public long TotalBytesFreed => Steps.Sum(s => s.BytesFreed);

    /// <summary>Espaço liberado formatado.</summary>
    public string FreedFormatted => ByteFormat.Format(TotalBytesFreed);

    /// <summary>Duração total.</summary>
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>Indica se tudo foi concluído sem falhas.</summary>
    public bool IsFullySuccessful => FailedCount == 0;

    /// <summary>
    /// Indica se algum passo foi ignorado por exigir administrador. A UI usa este
    /// sinal para oferecer a reexecução elevada (elevação sob demanda).
    /// </summary>
    public bool RequiresElevation { get; init; }

    /// <summary>Texto de resumo para o histórico de ações.</summary>
    public string Summary =>
        $"{SuccessCount} etapa(s) concluída(s), {SkippedCount} ignorada(s), {FailedCount} falha(s) · {FreedFormatted} liberados";
}

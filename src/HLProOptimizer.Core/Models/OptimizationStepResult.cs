namespace HLProOptimizer.Core.Models;

/// <summary>Resultado da execução de um passo de otimização.</summary>
/// <param name="StepId">Identificador do passo.</param>
/// <param name="StepName">Nome legível.</param>
/// <param name="Success">Se concluiu sem exceção.</param>
/// <param name="Message">Mensagem de resumo exibida no log.</param>
/// <param name="BytesFreed">Espaço liberado pelo passo (quando aplicável).</param>
/// <param name="Skipped">Se foi ignorado (ex.: exigia admin e o usuário recusou).</param>
/// <param name="Duration">Tempo de execução.</param>
/// <param name="Error">Erro capturado, quando houver.</param>
public sealed record OptimizationStepResult(
    string StepId,
    string StepName,
    bool Success,
    string Message,
    long BytesFreed = 0,
    bool Skipped = false,
    TimeSpan? Duration = null,
    string? Error = null);

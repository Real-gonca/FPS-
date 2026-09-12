namespace Nexus.Domain.Optimization;

/// <summary>
/// Resultado final de uma execução de tarefa.
/// <see cref="Before"/>/<see cref="After"/> reservam-se para benchmark A/B real
/// (medido, não estimado — spec §4.2); chegam com o patch de Otimização Rápida.
/// </summary>
public sealed record OptimizationResult(
    bool Success,
    string Message,
    OptimizationStatus Status,
    Guid? ActionId,
    Metrics.MetricValue? Before = null,
    Metrics.MetricValue? After = null)
{
    public static OptimizationResult Ok(string message, Guid actionId) =>
        new(true, message, OptimizationStatus.Success, actionId);

    public static OptimizationResult Fail(string message, Guid? actionId = null) =>
        new(false, message, OptimizationStatus.Failed, actionId);

    public static OptimizationResult RolledBack(string message, Guid actionId) =>
        new(false, message, OptimizationStatus.RolledBack, actionId);

    public static OptimizationResult Blocked(string message) =>
        new(false, message, OptimizationStatus.Blocked, null);
}

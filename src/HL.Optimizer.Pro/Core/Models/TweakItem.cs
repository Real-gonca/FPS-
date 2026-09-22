namespace HL.Optimizer.Pro.Core.Models;

public class TweakItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public OptimizationCategory Category { get; set; }
    public string CurrentState { get; set; } = "Desconhecido";
    public string RecommendedState { get; set; } = "";
    public bool IsApplied => CurrentState == RecommendedState;
    public OptimizationRisk Risk { get; set; } = OptimizationRisk.Seguro;
    public bool RequiresAdmin { get; set; }
    public bool Reversible { get; set; } = true;
    public string RegistryPath { get; set; } = "";
    public string RegistryValue { get; set; } = "";
    public object? ExpectedValue { get; set; }
    public object? DefaultValue { get; set; }
    public string Impact { get; set; } = "Baixo"; // Baixo, Médio, Alto
    public Func<Task<bool>>? CheckState { get; set; }
    public Func<Task<OptimizationResult>>? Apply { get; set; }
    public Func<Task<OptimizationResult>>? Revert { get; set; }
}

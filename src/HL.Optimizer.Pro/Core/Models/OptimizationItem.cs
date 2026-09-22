namespace HL.Optimizer.Pro.Core.Models;

public enum OptimizationRisk { Seguro, Moderado, Avancado }
public enum OptimizationCategory { Servicos, Inicializacao, Registro, Memoria, Cache, Logs, WindowsUpdate, Rede, Energia, Processos, DNS, Privacidade, Interface, Sistema }
public enum OptimizationStatus { Pendente, EmAndamento, Concluido, Falha, Ignorado }

public class OptimizationItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public OptimizationCategory Category { get; set; }
    public OptimizationRisk Risk { get; set; } = OptimizationRisk.Seguro;
    public bool Reversible { get; set; } = true;
    public bool RequiresAdmin { get; set; }
    public bool IsApplicable { get; set; } = true;
    public bool IsSelected { get; set; } = true;
    public OptimizationStatus Status { get; set; } = OptimizationStatus.Pendente;
    public string Command { get; set; } = "";
    public string RevertCommand { get; set; } = "";
    public string ResultMessage { get; set; } = "";
    public Func<Task<OptimizationResult>>? ExecuteAction { get; set; }
    public Func<Task<OptimizationResult>>? RevertAction { get; set; }
}

public class OptimizationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
}

public class OptimizationIndex
{
    public int TotalOptimizations { get; set; }
    public int CompletedOptimizations { get; set; }
    public double Percentage => TotalOptimizations > 0 ? (double)CompletedOptimizations / TotalOptimizations * 100 : 100;
    public string StatusText => Percentage switch
    {
        >= 80 => "Sistema saudável",
        >= 50 => "Atenção necessária",
        _ => "Otimização recomendada"
    };
    public List<OptimizationCategoryScore> CategoryScores { get; set; } = new();
}

public class OptimizationCategoryScore
{
    public OptimizationCategory Category { get; set; }
    public int Total { get; set; }
    public int Completed { get; set; }
    public double Score => Total > 0 ? (double)Completed / Total * 100 : 100;
}

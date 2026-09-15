namespace HLProOptimizer.Core.Models;

/// <summary>Estatísticas do gerenciador de inicialização.</summary>
/// <param name="TotalPrograms">Total de itens encontrados.</param>
/// <param name="EnabledCount">Itens ativos.</param>
/// <param name="DisabledCount">Itens desativados.</param>
/// <param name="HighImpactCount">Itens de alto impacto.</param>
/// <param name="OrphanedCount">Itens cujo executável não existe.</param>
/// <param name="CurrentBootSeconds">Tempo de boot estimado atual.</param>
/// <param name="OptimizedBootSeconds">Tempo estimado após desativar os não essenciais.</param>
public sealed record StartupSummary(
    int TotalPrograms,
    int EnabledCount,
    int DisabledCount,
    int HighImpactCount,
    int OrphanedCount,
    double CurrentBootSeconds,
    double OptimizedBootSeconds)
{
    /// <summary>Economia potencial em segundos.</summary>
    public double SavingsSeconds => Math.Max(0, CurrentBootSeconds - OptimizedBootSeconds);

    /// <summary>Percentual de redução do tempo de boot.</summary>
    public double SavingsPercent => CurrentBootSeconds <= 0
        ? 0
        : Math.Clamp((SavingsSeconds / CurrentBootSeconds) * 100d, 0d, 100d);
}

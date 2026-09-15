namespace HLProOptimizer.Infrastructure.Persistence.Entities;

/// <summary>
/// Estado persistente do Modo Gamer (tabela <c>GameModeStates</c>).
/// </summary>
/// <remarks>
/// Tabela de linha única (<see cref="SingletonRowId"/>): o estado é um snapshot
/// global, não uma coleção. Persistir permite restaurar os tweaks após o
/// aplicativo fechar no meio de uma sessão de jogo (queda de energia, por exemplo).
/// </remarks>
public class GameModeStateEntity
{
    /// <summary>Id fixo da linha única.</summary>
    public const int SingletonRowId = 1;

    /// <summary>Chave primária (sempre 1).</summary>
    public int Id { get; set; } = SingletonRowId;

    /// <summary>Se o Modo Gamer estava ativo ao fechar.</summary>
    public bool IsActive { get; set; }

    /// <summary>Momento da ativação.</summary>
    public DateTime? ActivatedAt { get; set; }

    /// <summary>GUID do plano de energia anterior (restaurado ao desativar).</summary>
    public string? PreviousPowerPlanGuid { get; set; }

    /// <summary>Memória liberada na última ativação.</summary>
    public long FreedMemoryBytes { get; set; }

    /// <summary>Estado completo serializado em JSON (tweaks aplicados/falhos).</summary>
    public string StateJson { get; set; } = "{}";

    /// <summary>Última gravação.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

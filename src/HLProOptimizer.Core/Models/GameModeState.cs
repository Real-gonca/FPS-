namespace HLProOptimizer.Core.Models;

/// <summary>Estado atual do Modo Gamer.</summary>
public sealed class GameModeState
{
    /// <summary>Se o modo está ativo.</summary>
    public bool IsActive { get; init; }

    /// <summary>Momento da ativação.</summary>
    public DateTime? ActivatedAt { get; init; }

    /// <summary>Tweaks aplicados com sucesso.</summary>
    public IReadOnlyList<GameTweak> AppliedTweaks { get; init; } = [];

    /// <summary>Tweaks que falharam.</summary>
    public IReadOnlyList<GameTweak> FailedTweaks { get; init; } = [];

    /// <summary>Processos finalizados ao ativar.</summary>
    public IReadOnlyList<string> TerminatedProcesses { get; init; } = [];

    /// <summary>Bytes de RAM liberados.</summary>
    public long FreedMemoryBytes { get; init; }

    /// <summary>Plano de energia anterior (para restaurar ao desativar).</summary>
    public string? PreviousPowerPlanGuid { get; init; }

    /// <summary>Mensagem de erro global, quando houver.</summary>
    public string? Error { get; init; }

    /// <summary>Estado inativo padrão.</summary>
    public static GameModeState Inactive { get; } = new();

    /// <summary>Tempo desde a ativação (ou zero).</summary>
    public TimeSpan ActiveDuration => ActivatedAt is { } at ? DateTime.Now - at : TimeSpan.Zero;
}

namespace HLProOptimizer.Core.Enums;

/// <summary>Classes de prioridade de processo (modo Gamer eleva o jogo).</summary>
public enum ProcessPriorityKind
{
    /// <summary>Ocioso.</summary>
    Idle = 0,

    /// <summary>Abaixo do normal.</summary>
    BelowNormal = 1,

    /// <summary>Normal.</summary>
    Normal = 2,

    /// <summary>Acima do normal.</summary>
    AboveNormal = 3,

    /// <summary>Alta (recomendado para jogos).</summary>
    High = 4,

    /// <summary>Tempo real (perigoso - uso restrito).</summary>
    RealTime = 5
}

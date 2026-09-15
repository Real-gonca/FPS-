namespace HLProOptimizer.Core.Enums;

/// <summary>Estado de uma operação assíncrona de longa duração.</summary>
public enum OperationState
{
    /// <summary>Ocioso.</summary>
    Idle = 0,

    /// <summary>Em execução.</summary>
    Running = 1,

    /// <summary>Concluído com sucesso.</summary>
    Completed = 2,

    /// <summary>Falhou.</summary>
    Failed = 3,

    /// <summary>Cancelado pelo usuário.</summary>
    Cancelled = 4
}

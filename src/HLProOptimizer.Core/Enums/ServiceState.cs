namespace HLProOptimizer.Core.Enums;

/// <summary>Estado atual de execução de um serviço Windows.</summary>
public enum ServiceState
{
    /// <summary>Parado.</summary>
    Stopped = 0,

    /// <summary>Iniciando.</summary>
    StartPending = 1,

    /// <summary>Parando.</summary>
    StopPending = 2,

    /// <summary>Em execução.</summary>
    Running = 3,

    /// <summary>Continuando.</summary>
    ContinuePending = 4,

    /// <summary>Pausado.</summary>
    Paused = 5,

    /// <summary>Estado não mapeado.</summary>
    Unknown = 6
}

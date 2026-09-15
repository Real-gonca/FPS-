using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>Representação independente de plataforma de um serviço Windows.</summary>
public sealed class WindowsServiceInfo
{
    /// <summary>Nome interno do serviço (ex.: "DiagTrack").</summary>
    public required string ServiceName { get; init; }

    /// <summary>Nome amigável.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Descrição registrada.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Estado atual.</summary>
    public ServiceState State { get; init; }

    /// <summary>Tipo de inicialização.</summary>
    public ServiceStartupKind StartupKind { get; init; }

    /// <summary>Se pode ser parado.</summary>
    public bool CanStop { get; init; }

    /// <summary>Se pode ser pausado.</summary>
    public bool CanPauseAndContinue { get; init; }

    /// <summary>Serviços que dependem deste.</summary>
    public IReadOnlyList<string> DependentServices { get; init; } = [];

    /// <summary>Serviços dos quais este depende.</summary>
    public IReadOnlyList<string> RequiredServices { get; init; } = [];

    /// <summary>PID do processo hospedeiro (0 quando parado).</summary>
    public int ProcessId { get; init; }

    /// <summary>Se é considerado crítico para o funcionamento do Windows.</summary>
    public bool IsCritical { get; init; }

    /// <summary>Se está em execução.</summary>
    public bool IsRunning => State == ServiceState.Running;
}

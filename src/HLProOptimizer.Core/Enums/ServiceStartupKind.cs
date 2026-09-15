namespace HLProOptimizer.Core.Enums;

/// <summary>
/// Tipo de inicialização de um serviço Windows.
/// Enum próprio para que o Core não dependa de <c>System.ServiceProcess</c>.
/// </summary>
public enum ServiceStartupKind
{
    /// <summary>Automático.</summary>
    Automatic = 0,

    /// <summary>Automático (início atrasado).</summary>
    AutomaticDelayed = 1,

    /// <summary>Manual.</summary>
    Manual = 2,

    /// <summary>Desabilitado.</summary>
    Disabled = 3,

    /// <summary>Desconhecido.</summary>
    Unknown = 4
}

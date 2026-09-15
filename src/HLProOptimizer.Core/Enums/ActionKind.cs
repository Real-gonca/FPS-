namespace HLProOptimizer.Core.Enums;

/// <summary>
/// Tipo de ação registrada no histórico (Ações Recentes do Dashboard).
/// </summary>
public enum ActionKind
{
    /// <summary>Análise/scan do sistema.</summary>
    Analysis = 0,

    /// <summary>Limpeza de arquivos/registro.</summary>
    Cleanup = 1,

    /// <summary>Otimização (quick/full/gamer/privacy).</summary>
    Optimization = 2,

    /// <summary>Ajustes de privacidade.</summary>
    Privacy = 3,

    /// <summary>Modo Gamer ativado/desativado.</summary>
    GameMode = 4,

    /// <summary>Gerenciador de inicialização.</summary>
    Startup = 5,

    /// <summary>Gerenciador de serviços.</summary>
    Services = 6,

    /// <summary>Gerenciador de drivers.</summary>
    Drivers = 7,

    /// <summary>Rede (DNS, TCP/IP, Winsock).</summary>
    Network = 8,

    /// <summary>Reparo de sistema (SFC, DISM, CHKDSK, Defender).</summary>
    Repair = 9,

    /// <summary>Backup / ponto de restauração.</summary>
    Backup = 10,

    /// <summary>Plano de energia.</summary>
    Power = 11,

    /// <summary>Alteração de configuração do app.</summary>
    Settings = 12
}

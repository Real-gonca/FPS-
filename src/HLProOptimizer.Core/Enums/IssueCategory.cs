namespace HLProOptimizer.Core.Enums;

/// <summary>
/// Categorias analisadas pelo Scan do Sistema (espelham as categorias da tela
/// "Análise do Sistema" e da "Limpeza").
/// </summary>
public enum IssueCategory
{
    /// <summary>Arquivos temporários (Windows Temp, %TEMP%, Prefetch).</summary>
    TemporaryFiles = 0,

    /// <summary>Cache do sistema (thumbnails, DirectX shaders, icon cache).</summary>
    SystemCache = 1,

    /// <summary>Logs, crash dumps e Windows Error Reporting.</summary>
    LogsAndDumps = 2,

    /// <summary>Registro do Windows (entradas órfãs/inválidas).</summary>
    Registry = 3,

    /// <summary>Programas de inicialização.</summary>
    StartupPrograms = 4,

    /// <summary>Serviços desnecessários ou mal configurados.</summary>
    Services = 5,

    /// <summary>Drivers desatualizados/problemáticos.</summary>
    Drivers = 6,

    /// <summary>Resíduos do Windows Update (WinSxS, SoftwareDistribution).</summary>
    WindowsUpdate = 7,

    /// <summary>Lixeira.</summary>
    RecycleBin = 8,

    /// <summary>Cache de navegadores (Chrome, Edge, Firefox, Brave, Opera).</summary>
    BrowserCache = 9,

    /// <summary>Privacidade e telemetria.</summary>
    Privacy = 10,

    /// <summary>Desempenho geral (plano de energia, RAM, disco).</summary>
    Performance = 11,

    /// <summary>Segurança (Defender, firewall, contas administrativas).</summary>
    Security = 12,

    /// <summary>Rede (DNS, TCP/IP, adaptadores).</summary>
    Network = 13
}

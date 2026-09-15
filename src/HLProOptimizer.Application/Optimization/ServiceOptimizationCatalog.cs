using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Application.Optimization;

/// <summary>
/// Catálogo de serviços Windows que podem ter seu tipo de inicialização
/// rebaixado com segurança, organizado por nível de agressividade.
/// </summary>
/// <remarks>
/// <para>
/// Nenhum serviço listado aqui é removido: apenas o <i>startup type</i> muda
/// (Automatic → Manual/Disabled) e o serviço continua podendo ser iniciado sob
/// demanda. Serviços críticos do kernel (RPC, DCOM, Plug and Play, etc.) nunca
/// aparecem nesta lista.
/// </para>
/// <para>
/// A ordem importa: serviços de telemetria entram no nível Balanceado, enquanto
/// serviços que alteram comportamento perceptível (indexação, Superfetch,
/// Windows Update) só no nível Agressivo.
/// </para>
/// </remarks>
public static class ServiceOptimizationCatalog
{
    /// <summary>Serviços de telemetria/diagnóstico (nível Balanceado).</summary>
    public static readonly IReadOnlyList<ServiceTweak> TelemetryServices =
    [
        new("DiagTrack", ServiceStartupKind.Disabled, "Telemetria e experiências conectadas do usuário."),
        new("dmwappushservice", ServiceStartupKind.Disabled, "Roteamento de mensagens WAP (push de telemetria)."),
        new("WerSvc", ServiceStartupKind.Manual, "Windows Error Reporting."),
        new("MapsBroker", ServiceStartupKind.Disabled, "Gerenciador de mapas baixados (pouco usado em desktop)."),
        new("RetailDemo", ServiceStartupKind.Disabled, "Serviço de demonstração de varejo."),
        new("lfsvc", ServiceStartupKind.Manual, "Serviço de geolocalização."),
        new("RemoteRegistry", ServiceStartupKind.Disabled, "Acesso remoto ao registro (risco de segurança)."),
        new("Fax", ServiceStartupKind.Disabled, "Serviço de fax.")
    ];

    /// <summary>Serviços do ecossistema Xbox (nível Balanceado; só afetam quem não joga na loja Xbox).</summary>
    public static readonly IReadOnlyList<ServiceTweak> XboxServices =
    [
        new("XblAuthManager", ServiceStartupKind.Manual, "Gerenciador de autenticação Xbox Live."),
        new("XblGameSave", ServiceStartupKind.Manual, "Save de jogos Xbox Live."),
        new("XboxNetApiSvc", ServiceStartupKind.Manual, "API de rede Xbox Live."),
        new("XboxGipSvc", ServiceStartupKind.Manual, "Serviço de acessórios Xbox.")
    ];

    /// <summary>Serviços com impacto perceptível (nível Agressivo).</summary>
    public static readonly IReadOnlyList<ServiceTweak> AggressiveServices =
    [
        new("WSearch", ServiceStartupKind.Manual, "Indexação de busca (libera I/O de disco em jogos)."),
        new("SysMain", ServiceStartupKind.Manual, "Superfetch/SysMain (pré-carregamento de apps)."),
        new("wuauserv", ServiceStartupKind.Manual, "Windows Update (pausa temporária durante sessões de jogo)."),
        new("TabletInputService", ServiceStartupKind.Manual, "Serviço de entrada para tablets/teclado virtual."),
        new("DiagSvc", ServiceStartupKind.Manual, "Execução de diagnósticos.")
    ];

    /// <summary>
    /// Serviços que NUNCA devem ser alterados (proteção explícita contra
    /// configuração incorreta do catálogo).
    /// </summary>
    public static readonly IReadOnlySet<string> ProtectedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "RPCSS", "DcomLaunch", "PlugPlay", "Winmgmt", "BrokerInfrastructure",
        "EventLog", "EventSystem", "LSM", "Power", "ProfSvc", "Schedule",
        "Spooler", "Themes", "UserManager", "Wcmsvc", "Winmgmt", "CryptSvc",
        "BFE", "MpsSvc", "WinDefend", "SecurityHealthService", "SENS", "ShellHWDetection",
        "AudioSrv", "AudioEndpointBuilder", "WudfSvc", "NlaSvc", "Dnscache", "Dhcp",
        "netprofm", "NSI", "TermService", "UmRdpService", "SessionEnv", "LanmanWorkstation",
        "LanmanServer", "GPSVC", "TrustedInstaller", "msiserver", "BITS", "FontCache", "srservice"
    };

    /// <summary>Retorna a lista de tweaks aplicáveis a um nível de agressividade.</summary>
    /// <param name="level">Nível selecionado.</param>
    /// <param name="includeXbox">Se serviços Xbox devem ser incluídos.</param>
    public static IReadOnlyList<ServiceTweak> ForLevel(OptimizationLevel level, bool includeXbox = true)
    {
        var result = new List<ServiceTweak>();

        if (level >= OptimizationLevel.Balanced)
        {
            result.AddRange(TelemetryServices);

            if (includeXbox)
            {
                result.AddRange(XboxServices);
            }
        }

        if (level >= OptimizationLevel.Aggressive)
        {
            result.AddRange(AggressiveServices);
        }

        return result
            .Where(t => !ProtectedServices.Contains(t.ServiceName))
            .GroupBy(t => t.ServiceName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Serviços considerados críticos: se parados, derrubam o score de estabilidade.</summary>
    public static readonly IReadOnlyList<string> CriticalServices =
    [
        "RPCSS", "DcomLaunch", "EventLog", "Winmgmt", "Schedule", "ProfSvc",
        "LSM", "Power", "UserManager", "CryptSvc", "BFE", "MpsSvc", "Dnscache", "NSI"
    ];
}

/// <summary>Definição de um ajuste de serviço.</summary>
/// <param name="ServiceName">Nome interno do serviço.</param>
/// <param name="TargetStartup">Tipo de inicialização desejado.</param>
/// <param name="Reason">Justificativa exibida na UI/log.</param>
public sealed record ServiceTweak(string ServiceName, ServiceStartupKind TargetStartup, string Reason);

using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Application.Privacy;

/// <summary>
/// Um ajuste de registro que materializa um item de privacidade.
/// </summary>
/// <param name="Hive">Colmeia do registro.</param>
/// <param name="KeyPath">Caminho da chave.</param>
/// <param name="ValueName">Nome do valor (DWORD).</param>
/// <param name="ProtectedValue">Valor que representa "protegido" (rastreamento desligado).</param>
/// <param name="DefaultValue">
/// Valor padrão do Windows. <c>null</c> significa que o valor deve ser REMOVIDO
/// ao restaurar os padrões (a chave não existe por padrão).
/// </param>
public sealed record PrivacyTweak(
    RegistryHiveKind Hive,
    string KeyPath,
    string ValueName,
    int ProtectedValue,
    int? DefaultValue);

/// <summary>
/// Definição completa de um item de privacidade controlável.
/// </summary>
/// <param name="Id">Identificador estável.</param>
/// <param name="DisplayName">Nome exibido na UI.</param>
/// <param name="Description">Explicação do efeito.</param>
/// <param name="Category">Grupo.</param>
/// <param name="RequiresAdmin">Se algum tweak toca HKLM.</param>
/// <param name="Recommended">Se o HL PRO OPTIMIZER recomenda proteger este item.</param>
/// <param name="RiskNote">Aviso de efeito colateral.</param>
/// <param name="PerformanceGainScore">Ganho de desempenho estimado (0-5).</param>
/// <param name="Tweaks">Ajustes de registro.</param>
/// <param name="ServiceName">Serviço associado (opcional).</param>
/// <param name="ProtectedServiceStartup">Startup type quando protegido.</param>
/// <param name="DefaultServiceStartup">Startup type padrão do Windows.</param>
public sealed record PrivacyDefinition(
    string Id,
    string DisplayName,
    string Description,
    PrivacyCategory Category,
    bool RequiresAdmin,
    bool Recommended,
    string RiskNote,
    int PerformanceGainScore,
    IReadOnlyList<PrivacyTweak> Tweaks,
    string? ServiceName = null,
    ServiceStartupKind ProtectedServiceStartup = ServiceStartupKind.Disabled,
    ServiceStartupKind DefaultServiceStartup = ServiceStartupKind.Manual);

/// <summary>
/// Catálogo de itens de privacidade do Windows 10/11.
/// </summary>
/// <remarks>
/// Todos os caminhos são os oficiais documentados pela Microsoft (políticas de
/// grupo e preferências do usuário). Nada aqui depende de versões específicas do
/// SO: valores ausentes são tratados como "estado padrão".
/// </remarks>
public static class PrivacyCatalog
{
    private const string DataCollectionPolicy = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    private const string DataCollectionMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection";
    private const string WerPolicy = @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting";
    private const string AdvertisingInfoUser = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
    private const string AdvertisingInfoPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo";
    private const string SystemPolicy = @"SOFTWARE\Policies\Microsoft\Windows\System";
    private const string SearchPolicy = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search";
    private const string CloudContentUser = @"Software\Microsoft\Windows\CurrentVersion\CloudContent";
    private const string PrivacyUser = @"Software\Microsoft\Windows\CurrentVersion\Privacy";
    private const string SiufRules = @"Software\Microsoft\Siuf\Rules";
    private const string AppPrivacyPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
    private const string BackgroundAppsUser = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    private const string SearchUser = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string InputPersonalization = @"Software\Microsoft\InputPersonalization";
    private const string DeliveryOptimization = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

    /// <summary>Todos os itens conhecidos, na ordem de exibição.</summary>
    public static IReadOnlyList<PrivacyDefinition> All { get; } =
    [
        // ------------------------------------------------ Telemetria
        new PrivacyDefinition(
            Id: "telemetry.diagtrack",
            DisplayName: "Connected User Experiences and Telemetry (DiagTrack)",
            Description: "Serviço responsável por coletar e enviar dados de diagnóstico e uso para a Microsoft.",
            Category: PrivacyCategory.Telemetry,
            RequiresAdmin: true,
            Recommended: true,
            RiskNote: "Sem este serviço, o Windows não envia telemetria. Alguns recursos de diagnóstico do Feedback Hub deixam de funcionar.",
            PerformanceGainScore: 3,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, DataCollectionPolicy, "AllowTelemetry", 0, null),
                new PrivacyTweak(RegistryHiveKind.LocalMachine, DataCollectionMachine, "AllowTelemetry", 0, null)
            ],
            ServiceName: "DiagTrack"),

        new PrivacyDefinition(
            Id: "telemetry.wappush",
            DisplayName: "WAP Push Message Routing Service",
            Description: "Roteia mensagens push de telemetria (dmwappushservice). Usado apenas para coleta de dados.",
            Category: PrivacyCategory.Telemetry,
            RequiresAdmin: true,
            Recommended: true,
            RiskNote: string.Empty,
            PerformanceGainScore: 1,
            Tweaks: [],
            ServiceName: "dmwappushservice"),

        new PrivacyDefinition(
            Id: "telemetry.errorreporting",
            DisplayName: "Windows Error Reporting",
            Description: "Envia relatórios de travamento (com dumps de memória) para a Microsoft.",
            Category: PrivacyCategory.Telemetry,
            RequiresAdmin: true,
            Recommended: true,
            RiskNote: "Você deixará de receber soluções automáticas para falhas de aplicativos.",
            PerformanceGainScore: 2,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, WerPolicy, "Disabled", 1, null)
            ],
            ServiceName: "WerSvc",
            ProtectedServiceStartup: ServiceStartupKind.Manual,
            DefaultServiceStartup: ServiceStartupKind.Manual),

        new PrivacyDefinition(
            Id: "telemetry.feedback",
            DisplayName: "Solicitações de feedback",
            Description: "Impede que o Windows pergunte periodicamente sua opinião sobre recursos (Feedback Hub).",
            Category: PrivacyCategory.Telemetry,
            RequiresAdmin: false,
            Recommended: true,
            RiskNote: string.Empty,
            PerformanceGainScore: 1,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.CurrentUser, SiufRules, "NumberOfSIUFInPeriod", 0, null),
                new PrivacyTweak(RegistryHiveKind.CurrentUser, SiufRules, "PeriodInNanoSeconds", 0, null)
            ]),

        new PrivacyDefinition(
            Id: "telemetry.suggestedapps",
            DisplayName: "Apps sugeridos e conteúdo do consumidor",
            Description: "Remove recomendações de aplicativos, anúncios no Menu Iniciar e dicas patrocinadas.",
            Category: PrivacyCategory.Telemetry,
            RequiresAdmin: false,
            Recommended: true,
            RiskNote: string.Empty,
            PerformanceGainScore: 1,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.CurrentUser, CloudContentUser, "DisableWindowsConsumerFeatures", 1, null),
                new PrivacyTweak(RegistryHiveKind.CurrentUser, CloudContentUser, "DisableSoftLanding", 1, null)
            ]),

        new PrivacyDefinition(
            Id: "telemetry.deliveryoptimization",
            DisplayName: "Delivery Optimization (P2P de updates)",
            Description: "Impede que seu PC envie partes de atualizações para outros computadores na internet.",
            Category: PrivacyCategory.Telemetry,
            RequiresAdmin: true,
            Recommended: true,
            RiskNote: "Atualizações podem demorar um pouco mais para baixar.",
            PerformanceGainScore: 2,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, DeliveryOptimization, "DODownloadMode", 0, null)
            ]),

        // ------------------------------------------------ Rastreamento
        new PrivacyDefinition(
            Id: "tracking.advertisingid",
            DisplayName: "Advertising ID",
            Description: "Desativa o identificador de anúncios usado para personalizar propaganda entre aplicativos.",
            Category: PrivacyCategory.Tracking,
            RequiresAdmin: false,
            Recommended: true,
            RiskNote: "Anúncios continuarão aparecendo, apenas não serão personalizados.",
            PerformanceGainScore: 0,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.CurrentUser, AdvertisingInfoUser, "Enabled", 0, 1),
                new PrivacyTweak(RegistryHiveKind.LocalMachine, AdvertisingInfoPolicy, "DisabledByGroupPolicy", 1, null)
            ]),

        new PrivacyDefinition(
            Id: "tracking.tailoredexperiences",
            DisplayName: "Experiências personalizadas com dados de diagnóstico",
            Description: "Impede que a Microsoft use seus dados de diagnóstico para personalizar dicas e anúncios.",
            Category: PrivacyCategory.Tracking,
            RequiresAdmin: false,
            Recommended: true,
            RiskNote: string.Empty,
            PerformanceGainScore: 0,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.CurrentUser, PrivacyUser, "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1)
            ]),

        new PrivacyDefinition(
            Id: "tracking.activityhistory",
            DisplayName: "Histórico de atividades e Timeline",
            Description: "Desativa a coleta e o envio do histórico de atividades (apps, documentos, sites) para a nuvem.",
            Category: PrivacyCategory.Tracking,
            RequiresAdmin: true,
            Recommended: true,
            RiskNote: "A Timeline (linha do tempo) deixará de mostrar atividades de outros dispositivos.",
            PerformanceGainScore: 2,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, SystemPolicy, "PublishUserActivities", 0, null),
                new PrivacyTweak(RegistryHiveKind.LocalMachine, SystemPolicy, "UploadUserActivities", 0, null)
            ]),

        new PrivacyDefinition(
            Id: "tracking.cortana",
            DisplayName: "Cortana e busca na web",
            Description: "Desativa a Cortana e impede que a busca do Windows envie consultas ao Bing.",
            Category: PrivacyCategory.Tracking,
            RequiresAdmin: true,
            Recommended: true,
            RiskNote: "A busca do Menu Iniciar deixará de exibir resultados da web.",
            PerformanceGainScore: 3,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, SearchPolicy, "AllowCortana", 0, null),
                new PrivacyTweak(RegistryHiveKind.LocalMachine, SearchPolicy, "AllowSearchToUseLocation", 0, null),
                new PrivacyTweak(RegistryHiveKind.LocalMachine, SearchPolicy, "DisableWebSearch", 1, null),
                new PrivacyTweak(RegistryHiveKind.CurrentUser, SearchUser, "BingSearchEnabled", 0, 1),
                new PrivacyTweak(RegistryHiveKind.CurrentUser, SearchUser, "CortanaConsent", 0, 1)
            ]),

        new PrivacyDefinition(
            Id: "tracking.inking",
            DisplayName: "Personalização de escrita e digitação",
            Description: "Desativa o dicionário pessoal que coleta o que você digita e escreve à mão.",
            Category: PrivacyCategory.Tracking,
            RequiresAdmin: false,
            Recommended: true,
            RiskNote: "Sugestões de texto personalizadas deixam de evoluir.",
            PerformanceGainScore: 0,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.CurrentUser, InputPersonalization, "AllowInputPersonalization", 0, 1),
                new PrivacyTweak(RegistryHiveKind.CurrentUser, InputPersonalization, "RestrictImplicitInkCollection", 1, null),
                new PrivacyTweak(RegistryHiveKind.CurrentUser, InputPersonalization, "RestrictImplicitTextCollection", 1, null)
            ]),

        new PrivacyDefinition(
            Id: "tracking.appdiagnostics",
            DisplayName: "Rastreamento de inicialização de aplicativos",
            Description: "Impede que o Windows registre quais aplicativos você abre e com que frequência.",
            Category: PrivacyCategory.Tracking,
            RequiresAdmin: true,
            Recommended: false,
            RiskNote: "O Menu Iniciar deixará de sugerir apps com base no uso.",
            PerformanceGainScore: 1,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, AppPrivacyPolicy, "LetAppsGetDiagnosticInfo", 2, null)
            ]),

        // ------------------------------------------------ Apps em segundo plano
        new PrivacyDefinition(
            Id: "background.global",
            DisplayName: "Apps em segundo plano (todos)",
            Description: "Impede que aplicativos da Store executem, recebam notificações e atualizem tiles em segundo plano.",
            Category: PrivacyCategory.BackgroundApps,
            RequiresAdmin: false,
            Recommended: true,
            RiskNote: "Apps da Store só atualizarão quando forem abertos.",
            PerformanceGainScore: 4,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.CurrentUser, BackgroundAppsUser, "GlobalUserDisabled", 1, null)
            ]),

        new PrivacyDefinition(
            Id: "background.policy",
            DisplayName: "Política de apps em segundo plano (máquina)",
            Description: "Aplica em nível de sistema a restrição de execução em segundo plano dos apps da Store.",
            Category: PrivacyCategory.BackgroundApps,
            RequiresAdmin: true,
            Recommended: false,
            RiskNote: "Afeta todos os usuários do computador.",
            PerformanceGainScore: 2,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2, null)
            ]),

        // ------------------------------------------------ Permissões
        new PrivacyDefinition(
            Id: "permission.location",
            DisplayName: "Acesso à localização",
            Description: "Nega aos aplicativos o acesso à sua localização geográfica.",
            Category: PrivacyCategory.Permissions,
            RequiresAdmin: true,
            Recommended: false,
            RiskNote: "Apps de mapas e previsão do tempo perderão a localização automática.",
            PerformanceGainScore: 1,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, AppPrivacyPolicy, "LetAppsAccessLocation", 2, null)
            ]),

        new PrivacyDefinition(
            Id: "permission.camera",
            DisplayName: "Acesso à câmera",
            Description: "Nega aos aplicativos em segundo plano o acesso à câmera (mantém o uso em primeiro plano).",
            Category: PrivacyCategory.Permissions,
            RequiresAdmin: true,
            Recommended: false,
            RiskNote: "Aplicativos de vídeo precisarão de permissão explícita.",
            PerformanceGainScore: 1,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, AppPrivacyPolicy, "LetAppsAccessCamera", 2, null)
            ]),

        new PrivacyDefinition(
            Id: "permission.microphone",
            DisplayName: "Acesso ao microfone",
            Description: "Nega aos aplicativos em segundo plano o acesso ao microfone.",
            Category: PrivacyCategory.Permissions,
            RequiresAdmin: true,
            Recommended: false,
            RiskNote: "Jogos e apps de voz (Discord, Teams) podem perder o áudio até você liberar a permissão.",
            PerformanceGainScore: 1,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, AppPrivacyPolicy, "LetAppsAccessMicrophone", 2, null)
            ]),

        new PrivacyDefinition(
            Id: "permission.notifications",
            DisplayName: "Acesso às notificações",
            Description: "Impede que aplicativos leiam suas notificações.",
            Category: PrivacyCategory.Permissions,
            RequiresAdmin: true,
            Recommended: true,
            RiskNote: string.Empty,
            PerformanceGainScore: 0,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, AppPrivacyPolicy, "LetAppsAccessNotifications", 2, null)
            ]),

        new PrivacyDefinition(
            Id: "permission.accountinfo",
            DisplayName: "Acesso às informações da conta",
            Description: "Impede que aplicativos leiam nome, imagem e dados da sua conta.",
            Category: PrivacyCategory.Permissions,
            RequiresAdmin: true,
            Recommended: true,
            RiskNote: string.Empty,
            PerformanceGainScore: 0,
            Tweaks:
            [
                new PrivacyTweak(RegistryHiveKind.LocalMachine, AppPrivacyPolicy, "LetAppsAccessAccountInfo", 2, null)
            ])
    ];

    /// <summary>Itens de uma categoria específica.</summary>
    /// <param name="category">Categoria desejada.</param>
    public static IReadOnlyList<PrivacyDefinition> ForCategory(PrivacyCategory category)
        => All.Where(d => d.Category == category).ToList();
}

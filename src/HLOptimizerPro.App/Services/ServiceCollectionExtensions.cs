using HLOptimizerPro.App.ViewModels;
using HLOptimizerPro.Core.Services;
using HLOptimizerPro.Core.Services.Abstractions;
using HLOptimizerPro.Core.Services.System;
using HLOptimizerPro.Core.Services.Tweaks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.App.Services;

/// <summary>
/// Registo de todas as dependências da aplicação (injeção de dependências).
///
/// Fonte única de verdade da composição: se um serviço novo for criado, é registado AQUI e fica
/// imediatamente disponível para todos os ViewModels. Nenhum ViewModel usa <c>new</c> direto sobre
/// serviços — o que mantém tudo testável.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHLOptimizerServices(this IServiceCollection services, ILoggerFactory loggerFactory)
    {
        // ---- Logging ---------------------------------------------------------
        services.AddSingleton(loggerFactory);
        services.AddLogging();

        // ---- Abstrações de sistema (permite substituir por duplos em testes) --
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IRegistryProvider, WindowsRegistryProvider>();
        services.AddSingleton<IFileSystemProvider, FileSystemProvider>();
        services.AddSingleton<IHardwareSensorProvider, LibreHardwareMonitorProvider>();

        // ---- Núcleo: configuração, contas e licenças -------------------------
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<LicenseService>();
        services.AddSingleton<ActivityLogService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<PrivilegeService>();

        // ---- Monitorização ---------------------------------------------------
        services.AddSingleton<MonitoringService>();
        services.AddSingleton<MonitoringHistoryService>();
        services.AddSingleton<SystemInfoService>();

        // ---- Avaliação e recomendações ---------------------------------------
        services.AddSingleton<ScoringService>();
        services.AddSingleton<SecurityService>();
        services.AddSingleton<StartupService>();
        services.AddSingleton<StorageService>();
        services.AddSingleton<SystemAssessmentService>();
        services.AddSingleton<RecommendationService>();

        // ---- Motor de otimizações --------------------------------------------
        services.AddSingleton<TweakFactory>();
        services.AddSingleton<OptimizationCatalog>();
        services.AddSingleton<OptimizationJournal>();
        services.AddSingleton<JournalReverter>();
        services.AddSingleton<RestorePointService>();
        services.AddSingleton<OptimizationEngine>();

        // ---- Limpeza e memória -----------------------------------------------
        services.AddSingleton<CleanupService>();
        services.AddSingleton<MemoryOptimizationService>();
        services.AddSingleton<AutoCleanupService>();

        // ---- Rede, DNS e latência --------------------------------------------
        services.AddSingleton<DnsBenchmarkService>();
        services.AddSingleton<NetworkService>();
        services.AddSingleton<LatencyService>();

        // ---- Apps, jogos e desempenho ----------------------------------------
        services.AddSingleton<ProcessOptimizationService>();
        services.AddSingleton<PowerPlanService>();
        services.AddSingleton<AppTunerService>();
        services.AddSingleton<GameModeService>();
        services.AddSingleton<PerformanceBoostService>();

        // ---- Relatórios e ações rápidas ---------------------------------------
        services.AddSingleton<ReportService>();
        services.AddSingleton<QuickActionsService>();

        // ---- Serviços de apresentação ----------------------------------------
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<UserFeedbackService>();
        services.AddSingleton<ShellStateService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<MicrosoftAuthService>();

        // ---- ViewModels ------------------------------------------------------
        RegisterViewModels(services);

        return services;
    }

    /// <summary>
    /// Registo dos ViewModels e das páginas navegáveis.
    ///
    /// As fábricas são registadas no <see cref="NavigationService"/> com as chaves usadas pela
    /// barra lateral — cada chave corresponde a exatamente uma página (todas navegáveis).
    /// </summary>
    private static void RegisterViewModels(IServiceCollection services)
    {
        // ViewModels transientes: cada navegação cria uma página limpa (sem estado acumulado).
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<AssessmentViewModel>();
        services.AddTransient<MemoryViewModel>();
        services.AddTransient<NetworkViewModel>();
        services.AddTransient<AppTunerViewModel>();
        services.AddTransient<TweaksViewModel>();
        services.AddTransient<GameModeViewModel>();
        services.AddTransient<LatencyViewModel>();
        services.AddTransient<OptimizationsViewModel>();
        services.AddTransient<PcBoostViewModel>();
        services.AddTransient<RecommendationsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<LoginViewModel>();
    }

    /// <summary>
    /// Associa cada chave de navegação à criação do ViewModel correspondente.
    /// Chamado depois de o contentor estar construído (precisa de resolver os ViewModels).
    /// </summary>
    public static void RegisterNavigationPages(this IServiceProvider provider)
    {
        var navigation = provider.GetRequiredService<INavigationService>();
        if (navigation is not NavigationService concrete) return;

        concrete.Register(NavigationKeys.Home, provider.GetRequiredService<DashboardViewModel>);
        concrete.Register(NavigationKeys.Assessment, provider.GetRequiredService<AssessmentViewModel>);
        concrete.Register(NavigationKeys.Memory, provider.GetRequiredService<MemoryViewModel>);
        concrete.Register(NavigationKeys.Network, provider.GetRequiredService<NetworkViewModel>);
        concrete.Register(NavigationKeys.AppTuner, provider.GetRequiredService<AppTunerViewModel>);
        concrete.Register(NavigationKeys.Tweaks, provider.GetRequiredService<TweaksViewModel>);
        concrete.Register(NavigationKeys.GameMode, provider.GetRequiredService<GameModeViewModel>);
        concrete.Register(NavigationKeys.Latency, provider.GetRequiredService<LatencyViewModel>);
        concrete.Register(NavigationKeys.Optimizations, provider.GetRequiredService<OptimizationsViewModel>);
        concrete.Register(NavigationKeys.PcBoost, provider.GetRequiredService<PcBoostViewModel>);
        concrete.Register(NavigationKeys.Recommendations, provider.GetRequiredService<RecommendationsViewModel>);
        concrete.Register(NavigationKeys.Settings, provider.GetRequiredService<SettingsViewModel>);
        concrete.Register(NavigationKeys.Help, provider.GetRequiredService<HelpViewModel>);
        concrete.Register(NavigationKeys.About, provider.GetRequiredService<AboutViewModel>);
    }
}

/// <summary>Chaves de navegação (evita strings soltas espalhadas pelo código).</summary>
public static class NavigationKeys
{
    public const string Home = "inicio";
    public const string Assessment = "avaliacao";
    public const string Memory = "ferramentas.memoria";
    public const string Network = "ferramentas.rede";
    public const string AppTuner = "ferramentas.apptuner";
    public const string Tweaks = "ferramentas.tweaks";
    public const string GameMode = "ferramentas.gamemode";
    public const string Latency = "ferramentas.latencia";
    public const string Optimizations = "otimizacoes";
    public const string PcBoost = "pcboost";
    public const string Recommendations = "recomendacoes";
    public const string Settings = "configuracao";
    public const string Help = "ajuda";
    public const string About = "sobre";
}

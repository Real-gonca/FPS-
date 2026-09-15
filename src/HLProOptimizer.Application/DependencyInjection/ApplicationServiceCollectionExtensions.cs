using HLProOptimizer.Application.Analysis;
using HLProOptimizer.Application.Analysis.Rules;
using HLProOptimizer.Application.Cleanup;
using HLProOptimizer.Application.Elevation;
using HLProOptimizer.Application.Elevation.Operations;
using HLProOptimizer.Application.GameMode;
using HLProOptimizer.Application.GameMode.Tweaks;
using HLProOptimizer.Application.History;
using HLProOptimizer.Application.Localization;
using HLProOptimizer.Application.Optimization;
using HLProOptimizer.Application.Optimization.Steps;
using HLProOptimizer.Application.Plugins;
using HLProOptimizer.Application.Privacy;
using HLProOptimizer.Application.Scoring;
using HLProOptimizer.Application.Settings;
using HLProOptimizer.Application.Startup;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace HLProOptimizer.Application.DependencyInjection;

/// <summary>
/// Composição da camada Application (casos de uso).
/// </summary>
/// <remarks>
/// Registra todos os serviços de negócio e suas estratégias (passos de
/// otimização, regras de análise, tweaks do Modo Gamer, operações elevadas).
/// As implementações dependentes de Windows (WMI, registro, powercfg...) vêm da
/// camada Infrastructure, registrada separadamente por
/// <c>AddHlOptimizerInfrastructure</c>.
/// </remarks>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registra todos os serviços da camada Application.</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <returns>A mesma coleção, para encadeamento (fluent API).</returns>
    public static IServiceCollection AddHlOptimizerApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCoreServices();
        services.AddOptimizationSteps();
        services.AddAnalysisRules();
        services.AddGameModeTweaks();
        services.AddElevatedOperations();

        return services;
    }

    /// <summary>Serviços centrais (localização, configurações, histórico, scoring, limpeza, análise...).</summary>
    private static void AddCoreServices(this IServiceCollection services)
    {
        // Localização e configurações
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ISettingsStore, JsonFileSettingsStore>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // Histórico e pontuação
        services.AddSingleton<IActionHistoryService, ActionHistoryService>();
        services.AddSingleton<ISystemScoreCalculator, SystemScoreCalculator>();

        // Casos de uso principais
        services.AddSingleton<ICleanupService, CleanupService>();
        services.AddSingleton<IOptimizationService, OptimizationService>();
        services.AddSingleton<ISystemAnalyzer, SystemAnalyzer>();
        services.AddSingleton<IPrivacyService, PrivacyService>();
        services.AddSingleton<IStartupManager, StartupManager>();
        services.AddSingleton<IPluginManager, DiskPluginManager>();

        // Modo Gamer (serviço + observador de sessões)
        services.AddSingleton<IGameModeService, GameModeService>();
        services.AddSingleton<GameSessionWatcher>();
        services.AddSingleton<IGameSessionWatcher>(provider => provider.GetRequiredService<GameSessionWatcher>());

        // Host de operações elevadas (elevação sob demanda)
        services.AddSingleton<IElevatedOperationHost, ElevatedOperationHost>();
    }

    /// <summary>Passos de otimização (padrão Command, compostos pelo orquestrador).</summary>
    private static void AddOptimizationSteps(this IServiceCollection services)
    {
        services.AddSingleton<IOptimizationStep, RestorePointStep>();
        services.AddSingleton<IOptimizationStep, TemporaryFilesStep>();
        services.AddSingleton<IOptimizationStep, SystemCacheStep>();
        services.AddSingleton<IOptimizationStep, BrowserCacheStep>();
        services.AddSingleton<IOptimizationStep, RecycleBinStep>();
        services.AddSingleton<IOptimizationStep, RegistryCleanupStep>();
        services.AddSingleton<IOptimizationStep, MemoryCompactStep>();
        services.AddSingleton<IOptimizationStep, DnsFlushStep>();
        services.AddSingleton<IOptimizationStep, NetworkLatencyStep>();
        services.AddSingleton<IOptimizationStep, ServiceOptimizationStep>();
        services.AddSingleton<IOptimizationStep, TelemetryStep>();
        services.AddSingleton<IOptimizationStep, StartupOptimizationStep>();
        services.AddSingleton<IOptimizationStep, GameBarStep>();
        services.AddSingleton<IOptimizationStep, MmcssStep>();
        services.AddSingleton<IOptimizationStep, IndexingStep>();
        services.AddSingleton<IOptimizationStep, VisualEffectsStep>();
        services.AddSingleton<IOptimizationStep, WindowsUpdatePauseStep>();
        services.AddSingleton<IOptimizationStep, UltimatePerformanceStep>();
        services.AddSingleton<IOptimizationStep, RestartExplorerStep>();
    }

    /// <summary>Regras de análise (padrão Strategy, executadas em paralelo).</summary>
    private static void AddAnalysisRules(this IServiceCollection services)
    {
        // Regra genérica de limpeza, registrada uma vez por categoria (DRY).
        services.AddSingleton<IAnalysisRule>(provider => new CleanupCategoryRule(
            IssueCategory.TemporaryFiles, "Cat_TemporaryFiles", 10, provider.GetRequiredService<ILocalizationService>()));

        services.AddSingleton<IAnalysisRule>(provider => new CleanupCategoryRule(
            IssueCategory.SystemCache, "Cat_SystemCache", 12, provider.GetRequiredService<ILocalizationService>()));

        services.AddSingleton<IAnalysisRule>(provider => new CleanupCategoryRule(
            IssueCategory.LogsAndDumps, "Cat_LogsAndDumps", 14, provider.GetRequiredService<ILocalizationService>()));

        services.AddSingleton<IAnalysisRule>(provider => new CleanupCategoryRule(
            IssueCategory.WindowsUpdate, "Cat_WindowsUpdate", 16, provider.GetRequiredService<ILocalizationService>(), requiresAdmin: true));

        services.AddSingleton<IAnalysisRule>(provider => new CleanupCategoryRule(
            IssueCategory.BrowserCache, "Cat_BrowserCache", 18, provider.GetRequiredService<ILocalizationService>()));

        services.AddSingleton<IAnalysisRule>(provider => new CleanupCategoryRule(
            IssueCategory.RecycleBin, "Cat_RecycleBin", 20, provider.GetRequiredService<ILocalizationService>()));

        services.AddSingleton<IAnalysisRule>(provider => new CleanupCategoryRule(
            IssueCategory.Registry, "Cat_Registry", 22, provider.GetRequiredService<ILocalizationService>()));

        // Regras específicas
        services.AddSingleton<IAnalysisRule, StartupAnalysisRule>();
        services.AddSingleton<IAnalysisRule, ServicesAnalysisRule>();
        services.AddSingleton<IAnalysisRule, DriversAnalysisRule>();
        services.AddSingleton<IAnalysisRule, PrivacyAnalysisRule>();
        services.AddSingleton<IAnalysisRule, PerformanceAnalysisRule>();
        services.AddSingleton<IAnalysisRule, SecurityAnalysisRule>();
        services.AddSingleton<IAnalysisRule, NetworkAnalysisRule>();
    }

    /// <summary>Tweaks do Modo Gamer.</summary>
    private static void AddGameModeTweaks(this IServiceCollection services)
    {
        // Delegam a passos de otimização já existentes.
        services.AddSingleton<IGameModeTweak, PowerPlanTweak>();
        services.AddSingleton<IGameModeTweak, GameBarTweak>();
        services.AddSingleton<IGameModeTweak, MmcssTweak>();
        services.AddSingleton<IGameModeTweak, IndexingTweak>();
        services.AddSingleton<IGameModeTweak, NetworkTweak>();
        services.AddSingleton<IGameModeTweak, WindowsUpdateTweak>();

        // Implementações próprias do Modo Gamer.
        services.AddSingleton<IGameModeTweak, UsbPowerTweak>();
        services.AddSingleton<IGameModeTweak, GpuOptimizationTweak>();
        services.AddSingleton<IGameModeTweak, NotificationsTweak>();
        services.AddSingleton<IGameModeTweak, BackgroundProcessesTweak>();
        services.AddSingleton<IGameModeTweak, MemoryTweak>();
        services.AddSingleton<IGameModeTweak, GamePriorityTweak>();
    }

    /// <summary>Operações executáveis em processo elevado.</summary>
    private static void AddElevatedOperations(this IServiceCollection services)
    {
        services.AddSingleton<IElevatedOperation, CommandElevatedOperation>();
        services.AddSingleton<IElevatedOperation, OptimizationElevatedOperation>();
        services.AddSingleton<IElevatedOperation, CleanupElevatedOperation>();
        services.AddSingleton<IElevatedOperation, GameModeElevatedOperation>();
        services.AddSingleton<IElevatedOperation, PrivacyElevatedOperation>();
        services.AddSingleton<IElevatedOperation, RestorePointElevatedOperation>();
        services.AddSingleton<IElevatedOperation, ServiceElevatedOperation>();
        services.AddSingleton<IElevatedOperation, HostsElevatedOperation>();
    }
}

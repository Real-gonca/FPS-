using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using HL.Optimizer.Pro.Core.Services;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.ViewModels;
using HL.Optimizer.Pro.Core.Utilities;

namespace HL.Optimizer.Pro;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();

        // Core Services
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IPerformanceMonitorService, PerformanceMonitorService>();
        services.AddSingleton<IOptimizationService, OptimizationService>();
        services.AddSingleton<ICleanupService, CleanupService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<INetworkService, NetworkService>();
        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IEmulatorDetectionService, EmulatorDetectionService>();
        services.AddSingleton<IRestoreService, RestoreService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IDiagnosisService, DiagnosisService>();
        services.AddSingleton<IPowerService, PowerService>();
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<BoosterViewModel>();
        services.AddTransient<TweaksViewModel>();
        services.AddTransient<CleanupViewModel>();
        services.AddTransient<GamesViewModel>();
        services.AddTransient<SystemInfoViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<PerformanceMonitorViewModel>();
        services.AddTransient<StartupViewModel>();
        services.AddTransient<ToolsViewModel>();
        services.AddTransient<RestoreViewModel>();
        services.AddTransient<SettingsViewModel>();

        Services = services.BuildServiceProvider();

        // Initialize log db
        var logService = Services.GetRequiredService<ILogService>();
        logService.Initialize();

        // Check admin
        AdminHelper.EnsureManifest();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is IDisposable d) d.Dispose();
        base.OnExit(e);
    }
}

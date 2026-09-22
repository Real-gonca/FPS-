using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace HL.Optimizer.Pro.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IPerformanceMonitorService _perfMonitor;
    private readonly IOptimizationService _optimization;
    private readonly ILogService _log;
    private readonly ILocalizationService _localization;

    [ObservableProperty] private BaseViewModel? _currentViewModel;
    [ObservableProperty] private string _currentPage = "Dashboard";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private SystemInfo? _systemInfoData;
    [ObservableProperty] private PerformanceMetrics? _currentMetrics;
    [ObservableProperty] private OptimizationIndex? _optimizationIndex;
    [ObservableProperty] private string _systemStatus = "Sistema saudável";
    [ObservableProperty] private ObservableCollection<LogEntry> _recentLogs = new();
    [ObservableProperty] private ObservableCollection<string> _notifications = new();
    [ObservableProperty] private string _selectedLanguage = "pt-BR";

    public List<string> AvailableLanguages { get; } = new() { "pt-BR", "pt-PT", "en-US", "es-ES" };

    public DashboardViewModel DashboardVM { get; }
    public BoosterViewModel BoosterVM { get; }
    public TweaksViewModel TweaksVM { get; }
    public CleanupViewModel CleanupVM { get; }
    public GamesViewModel GamesVM { get; }
    public SystemInfoViewModel SystemVM { get; }
    public DiagnosticsViewModel DiagnosticsVM { get; }
    public PerformanceMonitorViewModel PerformanceVM { get; }
    public StartupViewModel StartupVM { get; }
    public ToolsViewModel ToolsVM { get; }
    public RestoreViewModel RestoreVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public MainViewModel(
        ISystemInfoService systemInfo,
        IPerformanceMonitorService perfMonitor,
        IOptimizationService optimization,
        ILogService log,
        ILocalizationService localization,
        DashboardViewModel dashboardVM,
        BoosterViewModel boosterVM,
        TweaksViewModel tweaksVM,
        CleanupViewModel cleanupVM,
        GamesViewModel gamesVM,
        SystemInfoViewModel systemVM,
        DiagnosticsViewModel diagnosticsVM,
        PerformanceMonitorViewModel performanceVM,
        StartupViewModel startupVM,
        ToolsViewModel toolsVM,
        RestoreViewModel restoreVM,
        SettingsViewModel settingsVM)
    {
        _systemInfo = systemInfo;
        _perfMonitor = perfMonitor;
        _optimization = optimization;
        _log = log;
        _localization = localization;

        DashboardVM = dashboardVM;
        BoosterVM = boosterVM;
        TweaksVM = tweaksVM;
        CleanupVM = cleanupVM;
        GamesVM = gamesVM;
        SystemVM = systemVM;
        DiagnosticsVM = diagnosticsVM;
        PerformanceVM = performanceVM;
        StartupVM = startupVM;
        ToolsVM = toolsVM;
        RestoreVM = restoreVM;
        SettingsVM = settingsVM;

        _perfMonitor.MetricsUpdated += (s, m) => { CurrentMetrics = m; };

        CurrentViewModel = DashboardVM;
        IsAdmin = Core.Utilities.AdminHelper.IsAdministrator();

        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        try
        {
            SystemInfoData = await _systemInfo.GetSystemInfoAsync();
            OptimizationIndex = await _optimization.CalculateOptimizationIndexAsync();
            SystemStatus = OptimizationIndex.StatusText;

            _perfMonitor.StartMonitoring(1000);

            var logs = await _log.GetLogsAsync(10);
            RecentLogs = new ObservableCollection<LogEntry>(logs);

            await DashboardVM.InitializeAsync();
        }
        catch (Exception ex)
        {
            Notifications.Add($"Erro ao inicializar: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Navigate(string page)
    {
        CurrentPage = page;
        BaseViewModel vm = page switch
        {
            "Dashboard" => DashboardVM,
            "Booster" => BoosterVM,
            "Tweaks" => TweaksVM,
            "Cleanup" => CleanupVM,
            "Games" => GamesVM,
            "System" => SystemVM,
            "Diagnostics" => DiagnosticsVM,
            "Performance" => PerformanceVM,
            "Startup" => StartupVM,
            "Tools" => ToolsVM,
            "Restore" => RestoreVM,
            "Settings" => SettingsVM,
            _ => DashboardVM
        };

        CurrentViewModel = vm;
        await vm.InitializeAsync();
    }

    [RelayCommand]
    private void Search()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        // Global search implementation
        var term = SearchText.ToLower();
        // Navigate based on search
        if (term.Contains("limp") || term.Contains("clean"))
            _ = Navigate("Cleanup");
        else if (term.Contains("jog") || term.Contains("game"))
            _ = Navigate("Games");
        else if (term.Contains("rede") || term.Contains("network"))
            _ = Navigate("Tools");
        else if (term.Contains("inic") || term.Contains("startup"))
            _ = Navigate("Startup");
        else if (term.Contains("tweak"))
            _ = Navigate("Tweaks");
    }

    [RelayCommand]
    private void ChangeLanguage(string lang)
    {
        SelectedLanguage = lang;
        _localization.SetLanguage(lang);
    }

    [RelayCommand]
    private void Minimize() => Application.Current.MainWindow.WindowState = WindowState.Minimized;

    [RelayCommand]
    private void Maximize()
    {
        var win = Application.Current.MainWindow;
        win.WindowState = win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    [RelayCommand]
    private void Close() => Application.Current.Shutdown();

    [RelayCommand]
    private void OpenSettings() => _ = Navigate("Settings");

    [RelayCommand]
    private void ClearNotifications() => Notifications.Clear();
}

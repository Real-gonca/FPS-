using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Collections.ObjectModel;

namespace HL.Optimizer.Pro.ViewModels;

public partial class DashboardViewModel : BaseViewModel
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IPerformanceMonitorService _perfMonitor;
    private readonly IOptimizationService _optimization;
    private readonly ICleanupService _cleanup;
    private readonly IRestoreService _restore;
    private readonly ILogService _log;

    [ObservableProperty] private SystemInfo? _sysInfo;
    [ObservableProperty] private OptimizationIndex? _optIndex;
    [ObservableProperty] private PerformanceMetrics? _metrics;
    [ObservableProperty] private ObservableCollection<OptimizationItem> _optimizations = new();
    [ObservableProperty] private ObservableCollection<LogEntry> _recentActions = new();
    [ObservableProperty] private bool _isOptimizing;
    [ObservableProperty] private double _optimizationProgress;
    [ObservableProperty] private string _optimizationStatus = "";
    [ObservableProperty] private string _systemHealth = "Sistema saudável";
    [ObservableProperty] private double _healthPercentage = 0;
    [ObservableProperty] private List<QuickAction> _quickActions = new();

    public DashboardViewModel(
        ISystemInfoService systemInfo,
        IPerformanceMonitorService perfMonitor,
        IOptimizationService optimization,
        ICleanupService cleanup,
        IRestoreService restore,
        ILogService log)
    {
        _systemInfo = systemInfo;
        _perfMonitor = perfMonitor;
        _optimization = optimization;
        _cleanup = cleanup;
        _restore = restore;
        _log = log;

        _perfMonitor.MetricsUpdated += (s, m) => Metrics = m;

        QuickActions = new List<QuickAction>
        {
            new() { Name = "Limpeza", Icon = "🧹", Page = "Cleanup", Description = "Liberar espaço" },
            new() { Name = "Modo Jogo", Icon = "🎮", Page = "Games", Description = "Otimizar jogos" },
            new() { Name = "Inicialização", Icon = "⚡", Page = "Startup", Description = "Gerenciar boot" },
            new() { Name = "Rede", Icon = "🌐", Page = "Tools", Description = "Otimizar rede" },
            new() { Name = "Diagnóstico", Icon = "🔍", Page = "Diagnostics", Description = "Verificar sistema" },
            new() { Name = "Restauração", Icon = "♻", Page = "Restore", Description = "Pontos de restauração" },
        };
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            SysInfo = await _systemInfo.GetSystemInfoAsync();
            OptIndex = await _optimization.CalculateOptimizationIndexAsync();
            SystemHealth = OptIndex.StatusText;
            HealthPercentage = OptIndex.Percentage;

            var opts = await _optimization.GetAvailableOptimizationsAsync();
            Optimizations = new ObservableCollection<OptimizationItem>(opts.Where(o => !o.IsApplicable || true).Take(15));

            var logs = await _log.GetLogsAsync(10);
            RecentActions = new ObservableCollection<LogEntry>(logs);

            Metrics = _perfMonitor.CurrentMetrics;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ExecuteOptimization()
    {
        if (IsOptimizing) return;

        var result = System.Windows.MessageBox.Show(
            "Deseja criar um ponto de restauração antes de otimizar?\n\nRecomendado para segurança.",
            "HL Optimizer Pro - Ponto de Restauração",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Cancel) return;

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            OptimizationStatus = "Criando ponto de restauração...";
            var restoreCreated = await _restore.CreateRestorePointAsync($"HL Optimizer - Otimização {DateTime.Now:dd/MM/yyyy HH:mm}");
            if (!restoreCreated)
            {
                System.Windows.MessageBox.Show("Não foi possível criar ponto de restauração. Continuando mesmo assim.", "Aviso", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        IsOptimizing = true;
        OptimizationProgress = 0;
        var selected = Optimizations.Where(o => o.IsSelected).ToList();

        if (!selected.Any())
        {
            System.Windows.MessageBox.Show("Nenhuma otimização selecionada.", "HL Optimizer Pro", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            IsOptimizing = false;
            return;
        }

        var progress = new Progress<OptimizationProgress>(p =>
        {
            OptimizationProgress = p.Percentage;
            OptimizationStatus = $"Executando: {p.CurrentItem} ({p.Current}/{p.Total})";
        });

        try
        {
            var results = await _optimization.ExecuteOptimizationsAsync(selected, progress);
            var successCount = results.Count(r => r.Success);
            OptimizationStatus = $"Concluído: {successCount}/{results.Count} otimizações aplicadas com sucesso";

            // Refresh
            OptIndex = await _optimization.CalculateOptimizationIndexAsync();
            SystemHealth = OptIndex.StatusText;
            HealthPercentage = OptIndex.Percentage;

            var logs = await _log.GetLogsAsync(10);
            RecentActions = new ObservableCollection<LogEntry>(logs);

            System.Windows.MessageBox.Show($"{successCount} de {results.Count} otimizações aplicadas com sucesso!\n\n{string.Join("\n", results.Where(r => !r.Success).Select(r => $"✕ {r.Error}"))}",
                "HL Optimizer Pro - Otimização Concluída",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            OptimizationStatus = $"Erro: {ex.Message}";
            System.Windows.MessageBox.Show($"Erro durante otimização: {ex.Message}", "Erro", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    [RelayCommand]
    private void SelectAllOptimizations()
    {
        foreach (var opt in Optimizations) opt.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAllOptimizations()
    {
        foreach (var opt in Optimizations) opt.IsSelected = false;
    }
}

public class QuickAction
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Page { get; set; } = "";
    public string Description { get; set; } = "";
}

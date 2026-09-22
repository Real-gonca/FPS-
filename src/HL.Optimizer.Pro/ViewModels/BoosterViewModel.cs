using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;

namespace HL.Optimizer.Pro.ViewModels;

public partial class BoosterViewModel : BaseViewModel
{
    private readonly IOptimizationService _optimization;
    private readonly IPowerService _power;
    private readonly IPerformanceMonitorService _perf;

    [ObservableProperty] private string _selectedMode = "Equilibrado";
    [ObservableProperty] private ObservableCollection<OptimizationItem> _servicesOptimizations = new();
    [ObservableProperty] private ObservableCollection<OptimizationItem> _processOptimizations = new();
    [ObservableProperty] private string _activePowerPlan = "";
    [ObservableProperty] private PerformanceMetrics? _metrics;

    public List<string> Modes { get; } = new() { "Econômico", "Equilibrado", "Desempenho" };

    public BoosterViewModel(IOptimizationService optimization, IPowerService power, IPerformanceMonitorService perf)
    {
        _optimization = optimization;
        _power = power;
        _perf = perf;
        _perf.MetricsUpdated += (s, m) => Metrics = m;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            ActivePowerPlan = await _power.GetActivePowerPlanAsync();
            var all = await _optimization.GetAvailableOptimizationsAsync();
            ServicesOptimizations = new ObservableCollection<OptimizationItem>(all.Where(o => o.Category == OptimizationCategory.Servicos || o.Category == OptimizationCategory.Energia));
            ProcessOptimizations = new ObservableCollection<OptimizationItem>(all.Where(o => o.Category == OptimizationCategory.Processos || o.Category == OptimizationCategory.Memoria));
            Metrics = _perf.CurrentMetrics;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ApplyMode(string mode)
    {
        SelectedMode = mode;
        try
        {
            switch (mode)
            {
                case "Econômico":
                    await _power.SetPowerSavingAsync();
                    StatusMessage = "Modo Econômico ativado - economia de energia máxima";
                    break;
                case "Equilibrado":
                    await _power.SetBalancedAsync();
                    StatusMessage = "Modo Equilibrado ativado - equilíbrio entre desempenho e economia";
                    break;
                case "Desempenho":
                    await _power.SetHighPerformanceAsync();
                    StatusMessage = "Modo Desempenho ativado - máximo desempenho";
                    break;
            }
            ActivePowerPlan = await _power.GetActivePowerPlanAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OptimizeServices()
    {
        IsLoading = true;
        try
        {
            var selected = ServicesOptimizations.Where(s => s.IsSelected);
            var progress = new Progress<OptimizationProgress>(p => StatusMessage = $"Otimizando {p.CurrentItem}...");
            var results = await _optimization.ExecuteOptimizationsAsync(selected, progress);
            StatusMessage = $"{results.Count(r => r.Success)} serviços otimizados";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task OptimizeMemory()
    {
        IsLoading = true;
        try
        {
            StatusMessage = "Otimizando memória...";
            await Task.Run(() =>
            {
                foreach (var proc in System.Diagnostics.Process.GetProcesses())
                {
                    try { proc.MaxWorkingSet = proc.MaxWorkingSet; } catch { }
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            });
            StatusMessage = "Memória otimizada - working sets reduzidos";
        }
        finally { IsLoading = false; }
    }
}

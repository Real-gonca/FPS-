using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;

namespace HL.Optimizer.Pro.ViewModels;

public partial class PerformanceMonitorViewModel : BaseViewModel
{
    private readonly IPerformanceMonitorService _perf;

    [ObservableProperty] private PerformanceMetrics? _current;
    [ObservableProperty] private int _interval = 1000;
    [ObservableProperty] private PerformanceHistory _history = new();

    public List<int> Intervals { get; } = new() { 1000, 2000, 5000, 10000 };
    public List<string> IntervalLabels { get; } = new() { "1s", "2s", "5s", "10s" };

    public PerformanceMonitorViewModel(IPerformanceMonitorService perf)
    {
        _perf = perf;
        _perf.MetricsUpdated += (s, m) =>
        {
            Current = m;
            History = _perf.History;
        };
        Current = _perf.CurrentMetrics;
        History = _perf.History;
    }

    public override Task InitializeAsync()
    {
        _perf.StartMonitoring(Interval);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void SetInterval(int ms)
    {
        Interval = ms;
        _perf.SetInterval(ms);
        StatusMessage = $"Intervalo alterado para {ms}ms";
    }

    [RelayCommand]
    private void ClearHistory()
    {
        History.Metrics.Clear();
        StatusMessage = "Histórico limpo";
    }
}

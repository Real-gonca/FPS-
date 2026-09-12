using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using Microsoft.Extensions.Logging;
using Nexus.Application;
using Nexus.Application.Optimization;
using Nexus.Application.Recommendations;
using Nexus.Application.Telemetry;
using Nexus.Application.UseCases;
using Nexus.Domain.Metrics;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;
using SkiaSharp;

namespace Nexus.Presentation.ViewModels;

/// <summary>
/// ViewModel do Dashboard (spec §4.1): score, cards de métricas (todos com
/// N/D honesto), gráfico de tendência (LiveCharts2) e recomendações
/// ordenadas por impacto, com badge de risco e reversibilidade.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    private static readonly SKColor CpuColor = new(0x3B, 0x82, 0xF6);
    private static readonly SKColor RamColor = new(0x22, 0xD3, 0xEE);
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    /// <summary>Máximo de pontos no gráfico (downsampling para manter leve).</summary>
    private const int MaxChartPoints = 720;

    private readonly GetDashboardSnapshotUseCase _dashboardUseCase;
    private readonly ITelemetryHistory _history;
    private readonly ISettingsService _settings;
    private readonly RecommendationsEngine _recommendations;
    private readonly TelemetryEvents _telemetry;
    private readonly RunOptimizationUseCase _runOptimization;
    private readonly IOptimizationTaskCatalog _catalog;
    private readonly ILogger<DashboardViewModel> _log;

    private bool _refreshing;
    private SystemTelemetrySnapshot? _lastSnapshot;
    private StorageProbeResult? _lastTempProbe;

    public DashboardViewModel(
        GetDashboardSnapshotUseCase dashboardUseCase,
        ITelemetryHistory history,
        ISettingsService settings,
        RecommendationsEngine recommendations,
        TelemetryEvents telemetry,
        RunOptimizationUseCase runOptimization,
        IOptimizationTaskCatalog catalog,
        ILogger<DashboardViewModel> log)
    {
        _dashboardUseCase = dashboardUseCase;
        _history = history;
        _settings = settings;
        _recommendations = recommendations;
        _telemetry = telemetry;
        _runOptimization = runOptimization;
        _catalog = catalog;
        _log = log;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ApplyCommand = new AsyncRelayCommand<RecommendationRow>(ApplyAsync);

        _telemetry.SampleRecorded += OnTelemetrySample;
        _ = LoadInitialAsync();
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<RecommendationRow> ApplyCommand { get; }

    public ObservableCollection<RecommendationRow> Recommendations { get; } = new();

    public ObservableCollection<ISeries> ChartSeries { get; } = new();

    // ── Cards de métricas (texto + disponibilidade para cor N/D) ──────────
    private string _cpuText = MetricValue.NotAvailable;
    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }
    private bool _cpuAvailable;
    public bool CpuAvailable { get => _cpuAvailable; private set => Set(ref _cpuAvailable, value); }

    private string _ramText = MetricValue.NotAvailable;
    public string RamText { get => _ramText; private set => Set(ref _ramText, value); }
    private string _ramSubText = string.Empty;
    public string RamSubText { get => _ramSubText; private set => Set(ref _ramSubText, value); }
    private bool _ramAvailable;
    public bool RamAvailable { get => _ramAvailable; private set => Set(ref _ramAvailable, value); }

    private string _diskText = MetricValue.NotAvailable;
    public string DiskText { get => _diskText; private set => Set(ref _diskText, value); }
    private string _diskSubText = string.Empty;
    public string DiskSubText { get => _diskSubText; private set => Set(ref _diskSubText, value); }
    private bool _diskAvailable;
    public bool DiskAvailable { get => _diskAvailable; private set => Set(ref _diskAvailable, value); }

    private string _tempText = MetricValue.NotAvailable;
    public string TempText { get => _tempText; private set => Set(ref _tempText, value); }
    private string _tempSubText = string.Empty;
    public string TempSubText { get => _tempSubText; private set => Set(ref _tempSubText, value); }
    private bool _tempAvailable;
    public bool TempAvailable { get => _tempAvailable; private set => Set(ref _tempAvailable, value); }

    private string _processesText = MetricValue.NotAvailable;
    public string ProcessesText { get => _processesText; private set => Set(ref _processesText, value); }
    private bool _processesAvailable;
    public bool ProcessesAvailable { get => _processesAvailable; private set => Set(ref _processesAvailable, value); }

    private string _uptimeText = MetricValue.NotAvailable;
    public string UptimeText { get => _uptimeText; private set => Set(ref _uptimeText, value); }
    private bool _uptimeAvailable;
    public bool UptimeAvailable { get => _uptimeAvailable; private set => Set(ref _uptimeAvailable, value); }

    private double? _score;
    public double? Score { get => _score; private set => Set(ref _score, value); }
    private string _scoreNote = string.Empty;
    public string ScoreNote { get => _scoreNote; private set => Set(ref _scoreNote, value); }
    private string _chartNote = string.Empty;
    public string ChartNote { get => _chartNote; private set => Set(ref _chartNote, value); }
    private string _recommendationsNote = string.Empty;
    public string RecommendationsNote { get => _recommendationsNote; private set => Set(ref _recommendationsNote, value); }

    private async Task LoadInitialAsync() => await RefreshAsync();

    /// <summary>Recarrega dashboard completo (snapshot + score + recomendações + gráfico).</summary>
    public async Task RefreshAsync()
    {
        if (_refreshing)
            return;
        _refreshing = true;

        try
        {
            var payload = await _dashboardUseCase.ExecuteAsync();

            _lastSnapshot = payload.Snapshot;
            _lastTempProbe = payload.TempProbe; // guardado para o rebuild após mudança de modo
            ApplySnapshot(payload.Snapshot);
            Score = payload.Score.Score;
            ScoreNote = payload.Score.Summary;
            ApplyRecommendations(payload.Recommendations);
            await LoadChartAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha ao carregar o dashboard.");
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// Troca de modo Simples/Avançado: reconstrói as recomendações com o
    /// filtro de visibilidade (spec §7) sem refazer o varrimento completo.
    /// </summary>
    public void OnModeChanged()
    {
        if (_lastSnapshot is null)
        {
            _ = RefreshAsync();
            return;
        }

        var snapshot = _lastSnapshot;
        var probe = _lastTempProbe;
        _ = RebuildRecommendationsAsync(snapshot, probe);
    }

    private async Task RebuildRecommendationsAsync(SystemTelemetrySnapshot snapshot, StorageProbeResult? probe)
    {
        try
        {
            var settings = await _settings.LoadAsync();
            ApplyRecommendations(_recommendations.Build(snapshot, probe, settings.AdvancedMode));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falha ao reconstruir recomendações após mudança de modo.");
        }
    }

    private void OnTelemetrySample(object? sender, TelemetryEvents.SampleEvent e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        dispatcher.BeginInvoke(() =>
        {
            _lastSnapshot = e.Snapshot;
            ApplySnapshot(e.Snapshot);
            Score = e.Score.Score;
            ScoreNote = e.Score.Summary;
        });
    }

    private void ApplySnapshot(SystemTelemetrySnapshot s)
    {
        CpuText = s.CpuUsagePercent.IsAvailable ? s.CpuUsagePercent.Format("0") + " %" : MetricValue.NotAvailable;
        CpuAvailable = s.CpuUsagePercent.IsAvailable;

        RamText = s.RamFreeMb.IsAvailable
            ? (s.RamFreeMb.Value / 1024.0).ToString("0.0", Pt) + " GB"
            : MetricValue.NotAvailable;
        RamSubText = s.RamTotalMb.IsAvailable
            ? "de " + (s.RamTotalMb.Value / 1024.0).ToString("0", Pt) + " GB livres"
            : "memória RAM livre";
        RamAvailable = s.RamFreeMb.IsAvailable;

        DiskText = s.DiskFreeGb.IsAvailable ? s.DiskFreeGb.Format("0") + " GB" : MetricValue.NotAvailable;
        DiskSubText = s.DiskTotalGb.IsAvailable
            ? "de " + s.DiskTotalGb.Format("0") + " GB livres"
            : "espaço livre no disco principal";
        DiskAvailable = s.DiskFreeGb.IsAvailable;

        TempText = s.CpuTemperatureC.IsAvailable ? s.CpuTemperatureC.Format("0") + " °C" : MetricValue.NotAvailable;
        TempSubText = s.CpuTemperatureC.IsAvailable
            ? "zona térmica (amostragem 15 s)"
            : "fonte de temperatura indisponível";
        TempAvailable = s.CpuTemperatureC.IsAvailable;

        ProcessesText = s.ActiveProcessCount is { } p ? p.ToString(CultureInfo.InvariantCulture) : MetricValue.NotAvailable;
        ProcessesAvailable = s.ActiveProcessCount is not null;

        UptimeText = s.Uptime is { } u ? FormatUptime(u) : MetricValue.NotAvailable;
        UptimeAvailable = s.Uptime is not null;
    }

    private static string FormatUptime(TimeSpan u)
    {
        if (u.TotalDays >= 1)
            return $"{(int)u.TotalDays} d {u.Hours} h {u.Minutes} m";
        if (u.TotalHours >= 1)
            return $"{(int)u.TotalHours} h {u.Minutes} m";
        return $"{u.Minutes} m {u.Seconds} s";
    }

    private void ApplyRecommendations(IReadOnlyList<Recommendation> recommendations)
    {
        Recommendations.Clear();
        foreach (var r in recommendations)
        {
            Recommendations.Add(new RecommendationRow(r, _catalog.Has(r.TaskKey) && !r.IsAdvisory, ApplyCommand));
        }

        RecommendationsNote = recommendations.Count == 0
            ? "Sem recomendações pendentes neste momento (as regras só disparam com dados reais)."
            : "Ordenadas por impacto estimado. 'Em breve' = tarefa ainda não implementada neste patch.";
    }

    private async Task LoadChartAsync()
    {
        int hours = 12;
        try
        {
            hours = (await _settings.LoadAsync()).ChartWindowHours;
        }
        catch
        {
            // mantém 12 h
        }

        List<TelemetrySample> samples;
        try
        {
            samples = (await _history.GetRecentAsync(hours)).ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Falha ao ler o histórico de telemetria.");
            samples = new List<TelemetrySample>();
        }

        if (samples.Count == 0)
        {
            ChartSeries.Clear();
            ChartNote = "Ainda sem amostras — a primeira aparece em ~5 s (amostragem real a cada 5 s).";
            return;
        }

        int step = Math.Max(1, (int)Math.Ceiling(samples.Count / (double)MaxChartPoints));
        var picked = samples.Where((_, i) => i % step == 0).ToList();

        double[] x = picked.Select((_, i) => i * step * 5.0 / 60.0).ToArray(); // minutos desde o início da janela
        double[] cpu = picked.Select(s => s.CpuPercent ?? double.NaN).ToArray();
        double[] ram = picked.Select(s => s.RamUsedPercent ?? double.NaN).ToArray();

        ChartSeries.Clear();
        ChartSeries.Add(new LineSeries<double>
        {
            Name = "CPU (%)",
            XValues = x,
            YValues = cpu,
            Stroke = new SolidColorPaint(CpuColor, 2),
            GeometrySize = 0,
            Smoothed = false,
        });
        ChartSeries.Add(new LineSeries<double>
        {
            Name = "RAM usada (%)",
            XValues = x,
            YValues = ram,
            Stroke = new SolidColorPaint(RamColor, 2),
            GeometrySize = 0,
            Smoothed = false,
        });

        ChartNote = $"Amostragem real a cada 5 s · janela: últimas {hours} h · " +
                    "fontes: PerformanceCounter / WMI / DriveInfo.";
    }

    private async Task ApplyAsync(RecommendationRow row)
    {
        if (!row.CanApply || row.IsApplying)
            return;

        row.IsApplying = true;
        try
        {
            // O orquestrador emite a notificação tipada do desfecho.
            await _runOptimization.ExecuteAsync(row.TaskKey);
            await RefreshAsync();
        }
        finally
        {
            row.IsApplying = false;
        }
    }
}

/// <summary>Linha de recomendação (exposição à UI com estado de aplicação).</summary>
public sealed class RecommendationRow : ObservableObject
{
    private bool _isApplying;

    public RecommendationRow(Recommendation recommendation, bool canApply, IAsyncRelayCommand<RecommendationRow> applyCommand)
    {
        TaskKey = recommendation.TaskKey;
        Title = recommendation.Title;
        Description = recommendation.Description;
        ImpactPercent = recommendation.ImpactPercent;
        Risk = recommendation.Risk;
        RiskText = recommendation.Risk switch
        {
            RiskLevel.Low => "Baixo",
            RiskLevel.Medium => "Médio",
            _ => "Alto",
        };
        IsAdvisory = recommendation.IsAdvisory;
        IsReversible = recommendation.Reversible;
        CanApply = canApply;
        ApplyCommand = applyCommand;
        ImpactText = recommendation.IsAdvisory
            ? "Informativo"
            : "Impacto ~" + recommendation.ImpactPercent + " %";
    }

    public string TaskKey { get; }
    public string Title { get; }
    public string Description { get; }
    public int ImpactPercent { get; }
    public RiskLevel Risk { get; }
    public string RiskText { get; }
    public bool IsAdvisory { get; }
    public bool IsReversible { get; }
    public bool CanApply { get; }
    public IAsyncRelayCommand<RecommendationRow> ApplyCommand { get; }
    public string ImpactText { get; }

    public bool IsApplying
    {
        get => _isApplying;
        set
        {
            if (Set(ref _isApplying, value))
                OnPropertyChanged(nameof(ApplyButtonText));
        }
    }

    public string ApplyButtonText => IsApplying ? "A aplicar…" : CanApply ? "Aplicar" : "Em breve";
}

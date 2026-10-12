using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using HLOptimizerPro.Core.Services.Tweaks;
using Microsoft.Extensions.Logging;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>Item dos "Atalhos Rápidos" do Dashboard.</summary>
public sealed record QuickActionTile(string Key, string Title, string Description, string IconKind);

/// <summary>
/// ViewModel do Dashboard (3.2).
///
/// Princípio de honestidade dos dados: TODOS os valores apresentados vêm de medições reais
/// (PerformanceCounter/WMI/LibreHardwareMonitor). Quando um sensor não existe na máquina
/// (por exemplo GPU em certos portáteis), mostramos "N/D" e explicamos o motivo — nunca inventamos
/// um número para o gauge ficar bonito.
///
/// Desempenho: a medição corre num temporizador de 1 s com prioridade baixa. Se a leitura anterior
/// ainda não terminou, o tique é ignorado — a interface nunca bloqueia (requisito de aceitação).
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly MonitoringService _monitoring;
    private readonly MonitoringHistoryService _history;
    private readonly SystemInfoService _systemInfo;
    private readonly QuickActionsService _quickActions;
    private readonly SystemAssessmentService _assessment;
    private readonly RecommendationService _recommendations;
    private readonly OptimizationEngine _engine;
    private readonly OptimizationCatalog _catalog;
    private readonly RestorePointService _restorePoints;
    private readonly SettingsService _settings;
    private readonly AccountService _accounts;
    private readonly LicenseService _license;
    private readonly GameModeService _gameMode;
    private readonly ShellStateService _shell;
    private readonly IDialogService _dialogs;
    private readonly ILogger<DashboardViewModel> _logger;

    private readonly DispatcherTimer _metricsTimer;
    private readonly DispatcherTimer _chartTimer;
    private bool _reading;

    public DashboardViewModel(
        NotificationService notifications,
        MonitoringService monitoring,
        MonitoringHistoryService history,
        SystemInfoService systemInfo,
        QuickActionsService quickActions,
        SystemAssessmentService assessment,
        RecommendationService recommendations,
        OptimizationEngine engine,
        OptimizationCatalog catalog,
        RestorePointService restorePoints,
        SettingsService settings,
        AccountService accounts,
        LicenseService license,
        GameModeService gameMode,
        ShellStateService shell,
        IDialogService dialogs,
        ILogger<DashboardViewModel> logger)
        : base(notifications)
    {
        _monitoring = monitoring;
        _history = history;
        _systemInfo = systemInfo;
        _quickActions = quickActions;
        _assessment = assessment;
        _recommendations = recommendations;
        _engine = engine;
        _catalog = catalog;
        _restorePoints = restorePoints;
        _settings = settings;
        _accounts = accounts;
        _license = license;
        _gameMode = gameMode;
        _shell = shell;
        _dialogs = dialogs;
        _logger = logger;

        Title = "Início";
        Subtitle = "Visão geral do seu computador em tempo real";
        IconKind = "ViewDashboard";

        QuickActions = new ObservableCollection<QuickActionTile>
        {
            new("cleanup", "Limpeza Rápida", "Ficheiros temporários e cache", "Broom"),
            new("gamemode", "Modo Jogo", "Prioridade e energia para jogar", "ControllerClassic"),
            new("restore", "Restaurar Ponto", "Ponto de restauro do Windows", "History"),
            new("logs", "Logs", "Exportar diagnóstico do sistema", "FileDocumentOutline"),
        };

        BuildChart();

        var interval = Math.Clamp(_settings.Current.RealtimeIntervalMs, 500, 5000);
        _metricsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(interval),
        };
        _metricsTimer.Tick += (_, _) => ReadMetrics();

        _chartTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _chartTimer.Tick += (_, _) => RefreshChart();
    }

    // ---- Identificação --------------------------------------------------------

    public string Greeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            var part = hour switch
            {
                < 6 => "Boa madrugada",
                < 12 => "Bom dia",
                < 20 => "Boa tarde",
                _ => "Boa noite",
            };

            return $"{part}, {_accounts.Current.DisplayName}!";
        }
    }

    /// <summary>Resumo real de hardware/SO mostrado sob a saudação.</summary>
    public string MachineSummary => _systemInfo.Hardware.Summary;

    public bool IsPremium => _license.Current.IsPremium;

    // ---- Gauge CPU -----------------------------------------------------------
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private string _cpuValueText = "—";
    [ObservableProperty] private string _cpuSubText = "a medir…";

    // ---- Gauge RAM -----------------------------------------------------------
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private string _ramValueText = "—";
    [ObservableProperty] private string _ramSubText = "a medir…";
    [ObservableProperty] private bool _ramLowWarning;

    // ---- Gauge GPU -----------------------------------------------------------
    [ObservableProperty] private double _gpuPercent;
    [ObservableProperty] private string _gpuValueText = "N/D";
    [ObservableProperty] private string _gpuSubText = "sem sensor de GPU";
    [ObservableProperty] private bool _gpuAvailable;

    // ---- Cartões -------------------------------------------------------------
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private string _processCountText = "—";
    [ObservableProperty] private string _temperatureText = "—";
    [ObservableProperty] private string _diskText = "—";
    [ObservableProperty] private string _lastUpdateText = "a aguardar a primeira medição…";
    [ObservableProperty] private int _pendingOptimizations;

    public ObservableCollection<QuickActionTile> QuickActions { get; }

    /// <summary>Modelo do gráfico "Desempenho do Sistema" (últimas 24 h).</summary>
    public PlotModel PerformanceChart { get; private set; } = null!;

    /// <summary>Estado partilhado do shell (badge "Sistema otimizado" / "Atenção necessária").</summary>
    public ShellStateService Shell => _shell;

    public bool CountersAvailable => _monitoring.CountersAvailable;

    public string CountersWarning =>
        "Os contadores de desempenho do Windows não estão disponíveis nesta máquina " +
        "(é comum acontecer após algumas atualizações). Estamos a usar a leitura direta do sistema, " +
        "que é menos precisa para a CPU.";

    // ---- Ciclo de vida -------------------------------------------------------

    public override async Task OnNavigatedToAsync(object? parameter = null)
    {
        await base.OnNavigatedToAsync(parameter);

        ReadMetrics();
        RefreshChart();
        _metricsTimer.Start();
        _chartTimer.Start();

        // Mostra logo as otimizações pendentes sem bloquear a interface.
        _ = Task.Run(() =>
        {
            _catalog.Refresh();
            return _catalog.AvailableCount;
        }).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully) PendingOptimizations = task.Result;
        }, TaskScheduler.FromCurrentSynchronizationContext());

        // Se um jogo estiver em execução, o serviço de Modo Jogo já nos diz; o shell reflete-o.
        _shell.IsGameModeActive = _gameMode.Status.State == GameModeState.Ativo;
    }

    public override Task OnNavigatedFromAsync()
    {
        _metricsTimer.Stop();
        _chartTimer.Stop();
        return base.OnNavigatedFromAsync();
    }

    // ---- Medição em tempo real ------------------------------------------------

    /// <summary>
    /// Lê as métricas reais e atualiza os gauges. Corre no thread da interface (a leitura em si é
    /// rápida e já está protegida por try/catch dentro do MonitoringService).
    /// </summary>
    private void ReadMetrics()
    {
        if (_reading) return;   // evita acumular leituras se a anterior estiver lenta

        _reading = true;
        try
        {
            var metrics = _monitoring.Read();

            CpuPercent = metrics.CpuUsage * 100.0;
            CpuValueText = Formatters.Percent(metrics.CpuUsage, 0);

            var clock = metrics.CpuClockMhz > 0
                ? Formatters.Frequency(metrics.CpuClockMhz)
                : _systemInfo.Hardware.CpuName;

            CpuSubText = clock;

            var memory = metrics.Memory;
            RamPercent = memory.Usage * 100.0;
            RamValueText = Formatters.Percent(memory.Usage, 0);
            RamSubText = $"{Formatters.Bytes(memory.UsedBytes)} / {Formatters.Bytes(memory.TotalBytes)}";
            RamLowWarning = memory.FreePercent < AppConstants.MinFreeRamPercent;

            GpuAvailable = metrics.GpuUsageAvailable;
            if (metrics.GpuUsageAvailable)
            {
                GpuPercent = metrics.GpuUsage * 100.0;
                GpuValueText = Formatters.Percent(metrics.GpuUsage, 0);
                GpuSubText = metrics.GpuTotalVramBytes > 0
                    ? $"{Formatters.Bytes(metrics.GpuUsedVramBytes)} / {Formatters.Bytes(metrics.GpuTotalVramBytes)} VRAM"
                    : _systemInfo.Hardware.GpuName;
            }
            else
            {
                GpuValueText = "N/D";
                GpuSubText = "GPU sem contador de utilização";
            }

            UptimeText = Formatters.Uptime(_monitoring.GetUptime());
            ProcessCountText = $"{_monitoring.GetTopMemoryProcesses(64).Count} processos ativos";
            TemperatureText = Formatters.Temperature(metrics.CpuTemperatureCelsius ?? metrics.GpuTemperatureCelsius);

            var systemDisk = _systemInfo.Disks.FirstOrDefault(d => d.IsSystemDrive) ?? _systemInfo.Disks.FirstOrDefault();
            DiskText = systemDisk is { TotalBytes: > 0 }
                ? $"{Formatters.Bytes(systemDisk.FreeBytes)} livres de {Formatters.Bytes(systemDisk.TotalBytes)}"
                : "não detetado";

            LastUpdateText = $"Atualizado às {Formatters.ShortTime(metrics.Timestamp)} " +
                             $"(intervalo de {_metricsTimer.Interval.TotalSeconds:0.#} s)";

            // Registo no histórico (o serviço grava uma amostra por minuto, não a cada tique).
            _history.TryRecord(metrics);

            // Estado global refletido no rodapé da aplicação.
            _shell.UpdateFromMetrics(CpuPercent, RamPercent, metrics.CpuTemperatureCelsius);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler métricas no Dashboard.");
            LastUpdateText = "Falha temporária na leitura de métricas — a tentar novamente…";
        }
        finally
        {
            _reading = false;
        }
    }

    // ---- Gráfico de 24 h ------------------------------------------------------

    /// <summary>Cores alinhadas com o design system (azul CPU, roxo RAM, verde GPU).</summary>
    private static readonly OxyColor CpuColor = OxyColor.FromRgb(0x2D, 0x7B, 0xFF);
    private static readonly OxyColor RamColor = OxyColor.FromRgb(0x8B, 0x5C, 0xF6);
    private static readonly OxyColor GpuColor = OxyColor.FromRgb(0x3D, 0xD6, 0x8C);
    private static readonly OxyColor TextColor = OxyColor.FromRgb(0x8F, 0xA3, 0xC8);
    private static readonly OxyColor GridColor = OxyColor.FromArgb(0x28, 0x8F, 0xA3, 0xC8);

    private void BuildChart()
    {
        var model = new PlotModel
        {
            Background = OxyColors.Transparent,
            PlotAreaBackground = OxyColors.Transparent,
            PlotAreaBorderColor = GridColor,
            TextColor = TextColor,
            TitleColor = TextColor,
            DefaultFont = "Segoe UI",
            DefaultFontSize = 11,
            IsLegendVisible = true,
            LegendPosition = LegendPosition.TopRight,
            LegendTextColor = TextColor,
            LegendBackground = OxyColors.Transparent,
            LegendBorder = OxyColors.Transparent,
            Padding = new OxyThickness(2),
        };

        model.Axes.Add(new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            StringFormat = "HH:mm",
            TextColor = TextColor,
            TicklineColor = GridColor,
            AxislineColor = GridColor,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            IntervalType = DateTimeIntervalType.Hours,
            FontSize = 10,
            Title = null,
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = 0,
            Maximum = 100,
            StringFormat = "0 %",
            TextColor = TextColor,
            TicklineColor = GridColor,
            AxislineColor = GridColor,
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = GridColor,
            MinorGridlineStyle = LineStyle.None,
            FontSize = 10,
            Title = null,
        });

        model.Series.Add(CreateSeries("CPU", CpuColor));
        model.Series.Add(CreateSeries("RAM", RamColor));
        model.Series.Add(CreateSeries("GPU", GpuColor));

        PerformanceChart = model;
        OnPropertyChanged(nameof(PerformanceChart));
    }

    private static LineSeries CreateSeries(string title, OxyColor color) => new()
    {
        Title = title,
        Color = color,
        StrokeThickness = 1.8,
        LineJoin = LineJoin.Round,
        MarkerType = MarkerType.None,
        CanTrackerInterpolatePoints = false,
    };

    /// <summary>Recarrega as amostras reais das últimas 24 h gravadas pelo serviço de histórico.</summary>
    private void RefreshChart()
    {
        try
        {
            var samples = _history.GetSamples(AppConstants.HistoryWindowHours);

            if (PerformanceChart.Series.Count < 3) return;

            var cpu = (LineSeries)PerformanceChart.Series[0];
            var ram = (LineSeries)PerformanceChart.Series[1];
            var gpu = (LineSeries)PerformanceChart.Series[2];

            cpu.Points.Clear();
            ram.Points.Clear();
            gpu.Points.Clear();

            foreach (var sample in samples)
            {
                cpu.Points.Add(new DataPoint(DateTimeAxis.ToDouble(sample.Timestamp), sample.CpuPercent));
                ram.Points.Add(new DataPoint(DateTimeAxis.ToDouble(sample.Timestamp), sample.RamPercent));

                // A GPU só entra no gráfico quando foi realmente medida (0 significaria "sem sensor").
                if (sample.GpuPercent > 0)
                    gpu.Points.Add(new DataPoint(DateTimeAxis.ToDouble(sample.Timestamp), sample.GpuPercent));
            }

            ChartHasData = samples.Count > 1;
            PerformanceChart.InvalidatePlot(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao atualizar o gráfico de desempenho.");
        }
    }

    /// <summary>True quando já existem amostras suficientes para desenhar o gráfico.</summary>
    [ObservableProperty]
    private bool _chartHasData;

    public string ChartEmptyMessage =>
        "O histórico começa agora. A partir de uma hora de utilização este gráfico mostra o desempenho real " +
        "das últimas 24 h (uma amostra por minuto).";

    // ---- Comandos -------------------------------------------------------------

    /// <summary>Botão grande "Executar Otimização": avalia, aplica as ações recomendadas e reporta.</summary>
    [RelayCommand]
    private async Task RunOptimizationAsync()
    {
        var simulation = _catalog.SimulationMode;

        await RunBusyAsync(async () =>
        {
            BusyMessage = "A avaliar o sistema…";
            var report = await _assessment.AssessAsync(new Progress<string>(message => BusyMessage = message));

            var recommendations = _recommendations.Build(report);
            var actions = recommendations
                .Where(r => r.Priority == RecommendationPriority.Alta && !r.IsApplied)
                .SelectMany(_recommendations.ResolveActions)
                .DistinctBy(a => a.Key)
                .ToList();

            if (actions.Count == 0)
            {
                SetStatus($"O sistema já está otimizado. Pontuação atual: {report.OverallScoreText} ({report.Grade}).");
                return;
            }

            var requiresConfirmation = actions.Any(a => a.RequiresConfirmation);
            if (requiresConfirmation && _settings.Current.ConfirmRiskyActions && !simulation)
            {
                var confirmed = await _dialogs.ConfirmAsync(
                    "Confirmar otimização",
                    $"{actions.Count} otimizações de risco médio/alto vão ser aplicadas:\n\n" +
                    string.Join("\n", actions.Where(a => a.RequiresConfirmation).Select(a => $"• {a.Title} — {a.Risk}")) +
                    "\n\nUm ponto de restauro e o backup dos valores originais são criados antes de qualquer alteração.",
                    "Aplicar otimizações", danger: true);

                if (!confirmed)
                {
                    SetStatus("Otimização cancelada pelo utilizador.");
                    return;
                }
            }

            _engine.ProgressChanged += OnEngineProgress;

            try
            {
                var options = new OptimizationRunOptions
                {
                    BatchName = "Otimização Inteligente",
                    Simulation = simulation,
                    CreateRestorePoint = _settings.Current.CreateRestorePointBeforeChanges,
                    ActivityCategory = "Otimização",
                };

                var result = await _engine.ApplyAsync(actions, options);

                if (simulation)
                {
                    await _dialogs.ShowSimulationAsync("Modo Simular — o que seria alterado", _engine.Preview(actions));
                }

                Report(result, "Otimização Inteligente");

                if (result.Success)
                {
                    _catalog.Refresh();
                    PendingOptimizations = _catalog.AvailableCount;
                    _shell.UpdateSystemState(HealthLevel.Otimo);
                }
            }
            finally
            {
                _engine.ProgressChanged -= OnEngineProgress;
            }
        }, "A otimizar o sistema…");
    }

    private void OnEngineProgress(object? sender, OptimizationProgress progress)
        => BusyMessage = $"[{progress.Index}/{progress.Total}] {progress.ActionTitle}";

    /// <summary>Atalhos rápidos do Dashboard.</summary>
    [RelayCommand]
    private async Task RunQuickActionAsync(string? key)
    {
        switch (key)
        {
            case "cleanup":
                await RunBusyAsync(async () =>
                {
                    BusyMessage = "A limpar ficheiros temporários…";
                    var result = await _quickActions.QuickCleanupAsync();
                    Report(result, "Limpeza Rápida");
                }, "A limpar…");
                break;

            case "gamemode":
                var toggle = _quickActions.ToggleGameMode();
                Report(toggle, "Modo Jogo");
                _shell.IsGameModeActive = _gameMode.Status.State == GameModeState.Ativo;
                break;

            case "restore":
                await RunBusyAsync(async () =>
                {
                    BusyMessage = "A criar o ponto de restauro…";
                    var result = await _quickActions.CreateRestorePointAsync();
                    Report(result, "Ponto de Restauro");
                }, "A criar ponto de restauro…");
                break;

            case "logs":
                await RunBusyAsync(async () =>
                {
                    BusyMessage = "A recolher o diagnóstico…";
                    var result = await _quickActions.ExportSystemLogsAsync();

                    if (result.Success)
                    {
                        SetStatus(result.Message);
                        Notifications.Success("Logs exportados", result.Message);

                        var open = await _dialogs.ConfirmAsync(
                            "Logs exportados",
                            $"{result.Value}\n\nQuer abrir a pasta onde o ficheiro foi guardado?",
                            "Abrir pasta");
                        if (open) _quickActions.OpenDataFolder();
                    }
                    else
                    {
                        Report(result, "Logs");
                    }
                }, "A exportar logs…");
                break;
        }
    }

    /// <summary>Recomendações pendentes apresentadas no cartão de estado.</summary>
    public string PendingText => PendingOptimizations switch
    {
        0 => "Nenhuma otimização pendente — o sistema está no ponto.",
        1 => "1 otimização pendente — abra «Recomendações» para aplicar.",
        _ => $"{PendingOptimizations} otimizações pendentes — abra «Recomendações» para aplicar.",
    };

    partial void OnPendingOptimizationsChanged(int value) => OnPropertyChanged(nameof(PendingText));

    /// <summary>Estado da proteção do sistema / pontos de restauro, mostrado no cartão de segurança.</summary>
    [ObservableProperty]
    private string _restorePointText = "A verificar pontos de restauro…";

    [RelayCommand]
    private async Task CheckRestorePointsAsync()
    {
        await RunBusyAsync(() =>
        {
            var points = _restorePoints.GetRestorePoints();

            if (!_restorePoints.IsProtectionEnabled)
            {
                RestorePointText = "A proteção do sistema está desativada: sem pontos de restauro, " +
                                   "recomendamos ativar a proteção antes de aplicar otimizações.";
            }
            else if (points.Count == 0)
            {
                RestorePointText = "Proteção ativa, mas ainda não existem pontos de restauro neste computador.";
            }
            else
            {
                var (description, created) = points[0];
                RestorePointText = $"Último ponto: «{description}» ({created:dd/MM/yyyy HH:mm}) — " +
                                   $"{points.Count} ponto(s) disponível(is).";
            }

            SetStatus(RestorePointText);
            return Task.CompletedTask;
        }, "A verificar pontos de restauro…");
    }
}

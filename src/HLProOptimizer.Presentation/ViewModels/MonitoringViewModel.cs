using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Presentation.Controls;
using LiveChartsCore;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// Tela "Monitoramento": CPU, memória, GPU, disco e rede em tempo real
/// (1s por padrão), histórico em janela deslizante e tabela de processos com ações.
/// </summary>
/// <remarks>
/// <para>
/// <b>Uma fonte de verdade.</b> O <see cref="IPerformanceMonitor"/> já mantém o
/// <c>RingBuffer</c> de histórico; esta tela apenas assina
/// <see cref="IPerformanceMonitor.SampleCollected"/> e empurra os valores para as
/// coleções observadas pelo LiveCharts2. Nada de timers paralelos coletando métricas
/// (isso dobraria o custo de WMI/PerformanceCounter).
/// </para>
/// <para>
/// <b>Processos com cadência menor.</b> Listar 200 processos a cada segundo congela a
/// UI; atualizamos a tabela a cada 5 amostras (ou sob demanda), mantendo os gráficos
/// em 1s.
/// </para>
/// </remarks>
public sealed partial class MonitoringViewModel : ViewModelBase
{
    private const int ProcessRefreshEvery = 5;

    private readonly IPerformanceMonitor _monitor;
    private readonly IProcessService _processes;
    private readonly ISystemInformationService _systemInformation;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly ICollectionView _processView;

    private readonly ObservableCollection<double> _cpuHistory = [];
    private readonly ObservableCollection<double> _memoryHistory = [];
    private readonly ObservableCollection<double> _gpuHistory = [];
    private readonly ObservableCollection<double> _diskReadHistory = [];
    private readonly ObservableCollection<double> _diskWriteHistory = [];
    private readonly ObservableCollection<double> _downloadHistory = [];
    private readonly ObservableCollection<double> _uploadHistory = [];
    private readonly ObservableCollection<string> _timeLabels = [];

    private int _sampleCounter;
    private bool _hooked;

    /// <summary>Cria o ViewModel de monitoramento.</summary>
    /// <param name="monitor">Monitor de desempenho.</param>
    /// <param name="processes">Serviço de processos.</param>
    /// <param name="systemInformation">Inventário (partições, memória total).</param>
    /// <param name="settings">Configurações (intervalo/histórico).</param>
    /// <param name="dialogs">Diálogos.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public MonitoringViewModel(
        IPerformanceMonitor monitor,
        IProcessService processes,
        ISystemInformationService systemInformation,
        ISettingsService settings,
        IDialogService dialogs,
        ILocalizationService localization,
        ILogger<MonitoringViewModel> logger)
        : base(localization, logger)
    {
        _monitor = monitor;
        _processes = processes;
        _systemInformation = systemInformation;
        _settings = settings;
        _dialogs = dialogs;

        _processView = CollectionViewSource.GetDefaultView(Processes);
        _processView.SortDescriptions.Add(new SortDescription(nameof(ProcessSnapshot.CpuPercent), ListSortDirection.Descending));

        CpuSeries = [ChartTheme.Line(_cpuHistory, ChartTheme.Accent, L("Mon_Cpu"))];
        MemorySeries = [ChartTheme.Line(_memoryHistory, ChartTheme.Primary, L("Mon_Memory"))];
        GpuSeries = [ChartTheme.Line(_gpuHistory, ChartTheme.Success, L("Mon_Gpu"))];
        DiskSeries =
        [
            ChartTheme.Line(_diskReadHistory, ChartTheme.Warning, L("Mon_Read"), fillArea: false),
            ChartTheme.Line(_diskWriteHistory, ChartTheme.Danger, L("Mon_Write"), fillArea: false)
        ];
        NetworkSeries =
        [
            ChartTheme.Line(_downloadHistory, ChartTheme.Purple, L("Mon_Download"), fillArea: false),
            ChartTheme.Line(_uploadHistory, ChartTheme.Accent, L("Mon_Upload"), fillArea: false)
        ];

        TimeAxes = ChartTheme.TimeAxes(_timeLabels);
        PercentAxes = ChartTheme.PercentAxes();
        RateAxes = ChartTheme.AutoAxes();

        IntervalSeconds = ClampInterval(_settings.Current.MonitoringIntervalSeconds);
        HistorySeconds = _settings.Current.MonitoringHistorySeconds;
    }

    /// <summary>Séries de CPU.</summary>
    public ISeries[] CpuSeries { get; }

    /// <summary>Séries de memória.</summary>
    public ISeries[] MemorySeries { get; }

    /// <summary>Séries de GPU.</summary>
    public ISeries[] GpuSeries { get; }

    /// <summary>Séries de disco (leitura/escrita em MB/s).</summary>
    public ISeries[] DiskSeries { get; }

    /// <summary>Séries de rede (download/upload em KB/s).</summary>
    public ISeries[] NetworkSeries { get; }

    /// <summary>Eixos X (tempo).</summary>
    public Axis[] TimeAxes { get; }

    /// <summary>Eixos Y de percentual.</summary>
    public Axis[] PercentAxes { get; }

    /// <summary>Eixos Y de taxa.</summary>
    public Axis[] RateAxes { get; }

    /// <summary>Processos exibidos na tabela.</summary>
    public ObservableCollection<ProcessSnapshot> Processes { get; } = [];

    /// <summary>Visão ordenada dos processos.</summary>
    public ICollectionView ProcessView => _processView;

    /// <summary>Partições de disco.</summary>
    public ObservableCollection<DiskPartition> Partitions { get; } = [];

    /// <summary>Opções de intervalo suportadas (segundos).</summary>
    public int[] IntervalOptions { get; } = [1, 2, 5];

    /// <summary>Uso de CPU (%).</summary>
    [ObservableProperty]
    private double _cpuPercent;

    /// <summary>Temperatura da CPU.</summary>
    [ObservableProperty]
    private string _cpuTemperatureText = "—";

    /// <summary>Clock da CPU.</summary>
    [ObservableProperty]
    private string _cpuClockText = "—";

    /// <summary>Uso de memória (%).</summary>
    [ObservableProperty]
    private double _memoryPercent;

    /// <summary>Memória em uso.</summary>
    [ObservableProperty]
    private string _memoryInUseText = "—";

    /// <summary>Memória disponível.</summary>
    [ObservableProperty]
    private string _memoryAvailableText = "—";

    /// <summary>Memória total.</summary>
    [ObservableProperty]
    private string _memoryTotalText = "—";

    /// <summary>Commit charge.</summary>
    [ObservableProperty]
    private string _commitText = "—";

    /// <summary>Uso de GPU (%).</summary>
    [ObservableProperty]
    private double _gpuPercent;

    /// <summary>Temperatura da GPU.</summary>
    [ObservableProperty]
    private string _gpuTemperatureText = "—";

    /// <summary>VRAM em uso.</summary>
    [ObservableProperty]
    private string _gpuMemoryText = "—";

    /// <summary>Uso de disco (%).</summary>
    [ObservableProperty]
    private double _diskPercent;

    /// <summary>Taxa de leitura.</summary>
    [ObservableProperty]
    private string _diskReadText = "0 KB/s";

    /// <summary>Taxa de escrita.</summary>
    [ObservableProperty]
    private string _diskWriteText = "0 KB/s";

    /// <summary>Download.</summary>
    [ObservableProperty]
    private string _networkDownloadText = "0 KB/s";

    /// <summary>Upload.</summary>
    [ObservableProperty]
    private string _networkUploadText = "0 KB/s";

    /// <summary>Latência medida.</summary>
    [ObservableProperty]
    private string _latencyText = "—";

    /// <summary>Quantidade de processos.</summary>
    [ObservableProperty]
    private int _processCount;

    /// <summary>Quantidade de threads.</summary>
    [ObservableProperty]
    private int _threadCount;

    /// <summary>Intervalo de atualização (segundos).</summary>
    [ObservableProperty]
    private int _intervalSeconds = 1;

    /// <summary>Janela de histórico (segundos).</summary>
    [ObservableProperty]
    private int _historySeconds = 60;

    /// <summary>Indica se a coleta está ativa.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    private bool _isRunning;

    /// <summary>Indica se a coleta está pausada.</summary>
    public bool IsPaused => !IsRunning;

    /// <summary>Momento da última amostra.</summary>
    [ObservableProperty]
    private string _lastSampleText = "—";

    /// <summary>Processo selecionado na tabela.</summary>
    [ObservableProperty]
    private ProcessSnapshot? _selectedProcess;

    /// <summary>Critério de ordenação da tabela.</summary>
    [ObservableProperty]
    private string _sortBy = "cpu";

    /// <inheritdoc />
    public override void OnNavigatedTo(object? parameter)
    {
        if (!_hooked)
        {
            _monitor.SampleCollected += OnSampleCollected;
            _hooked = true;
        }

        _ = StartAsync();
        _ = LoadStaticInfoAsync();
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom()
    {
        _ = StopAsync();

        if (_hooked)
        {
            _monitor.SampleCollected -= OnSampleCollected;
            _hooked = false;
        }
    }

    /// <summary>Inicia a coleta.</summary>
    [RelayCommand]
    private async Task StartAsync()
    {
        try
        {
            await _monitor.StartAsync(TimeSpan.FromSeconds(IntervalSeconds), HistorySeconds).ConfigureAwait(true);

            IsRunning = _monitor.IsRunning;

            // Preenche os gráficos com o histórico já coletado (retomada sem "buraco").
            SeedHistory();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao iniciar o monitoramento.");

            StatusMessage = L("Msg_GenericError");
        }
    }

    /// <summary>Interrompe a coleta.</summary>
    [RelayCommand]
    private async Task StopAsync()
    {
        try
        {
            await _monitor.StopAsync().ConfigureAwait(true);

            IsRunning = _monitor.IsRunning;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao interromper o monitoramento.");
        }
    }

    /// <summary>Alterna pausa/retomada.</summary>
    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        if (IsRunning)
        {
            await StopAsync().ConfigureAwait(true);
            StatusMessage = L("Mon_Paused");
        }
        else
        {
            await StartAsync().ConfigureAwait(true);
            StatusMessage = string.Empty;
        }
    }

    /// <summary>Aplica um novo intervalo de atualização.</summary>
    /// <param name="seconds">Intervalo em segundos (1, 2 ou 5).</param>
    [RelayCommand]
    private async Task SetIntervalAsync(string? seconds)
    {
        if (!int.TryParse(seconds, out var value))
        {
            return;
        }

        IntervalSeconds = ClampInterval(value);

        _monitor.SetInterval(TimeSpan.FromSeconds(IntervalSeconds));

        var settings = _settings.Current.Clone();
        settings.MonitoringIntervalSeconds = IntervalSeconds;
        settings.MonitoringHistorySeconds = HistorySeconds;

        await _settings.SaveAsync(settings).ConfigureAwait(true);
    }

    /// <summary>Limpa o histórico dos gráficos.</summary>
    [RelayCommand]
    private void ClearHistory()
    {
        _monitor.ClearHistory();

        OnUiThread(() =>
        {
            Clear(_cpuHistory);
            Clear(_memoryHistory);
            Clear(_gpuHistory);
            Clear(_diskReadHistory);
            Clear(_diskWriteHistory);
            Clear(_downloadHistory);
            Clear(_uploadHistory);
            Clear(_timeLabels);
        });
    }

    /// <summary>Atualiza a tabela de processos.</summary>
    [RelayCommand]
    private async Task RefreshProcessesAsync() => await LoadProcessesAsync().ConfigureAwait(true);

    /// <summary>Finaliza o processo selecionado.</summary>
    /// <param name="process">Processo.</param>
    [RelayCommand]
    private async Task KillProcessAsync(ProcessSnapshot? process)
    {
        process ??= SelectedProcess;

        if (process is null)
        {
            return;
        }

        if (process.IsCritical)
        {
            await _dialogs.ShowWarningAsync(L("Mon_KillProcess"), LF("Mon_CannotKillCritical", process.Name)).ConfigureAwait(true);
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            L("Mon_KillConfirm"),
            LF("Mon_KillMessage", process.Name, process.Id),
            confirmText: L("Mon_KillProcess"),
            isDestructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var killed = await _processes.TerminateAsync(process.Id).ConfigureAwait(true);

            StatusMessage = killed
                ? LF("Mon_ProcessTerminated", process.Name)
                : LF("Mon_ProcessNotTerminated", process.Name);

            if (killed)
            {
                await LoadProcessesAsync().ConfigureAwait(true);
            }
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Aplica prioridade alta ao processo selecionado.</summary>
    /// <param name="process">Processo.</param>
    [RelayCommand]
    private async Task BoostProcessAsync(ProcessSnapshot? process)
    {
        process ??= SelectedProcess;

        if (process is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var boosted = await _processes.BoostAsync(process.Name).ConfigureAwait(true);

            StatusMessage = boosted ? LF("Mon_ProcessBoosted", process.Name) : L("Msg_NeedAdmin");
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Define a prioridade de um processo.</summary>
    /// <param name="priority">Nome do <see cref="ProcessPriorityKind"/>.</param>
    [RelayCommand]
    private async Task SetPriorityAsync(string? priority)
    {
        var process = SelectedProcess;

        if (process is null || string.IsNullOrWhiteSpace(priority) ||
            !Enum.TryParse<ProcessPriorityKind>(priority, ignoreCase: true, out var kind))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var applied = await _processes.SetPriorityAsync(process.Id, kind).ConfigureAwait(true);

            StatusMessage = applied
                ? LF("Mon_PriorityApplied", process.Name, Localization.GetEnumText(kind))
                : L("Msg_NeedAdmin");
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Abre o local do executável do processo selecionado.</summary>
    [RelayCommand]
    private async Task OpenProcessLocationAsync()
    {
        var path = SelectedProcess?.ExecutablePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await Task.Run(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            })).ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    partial void OnSortByChanged(string value)
    {
        _processView.SortDescriptions.Clear();

        var property = value.Equals("memory", StringComparison.OrdinalIgnoreCase)
            ? nameof(ProcessSnapshot.MemoryBytes)
            : nameof(ProcessSnapshot.CpuPercent);

        _processView.SortDescriptions.Add(new SortDescription(property, ListSortDirection.Descending));
        _processView.Refresh();
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    private void OnSampleCollected(object? sender, PerformanceSample sample) => OnUiThread(() => ApplySample(sample));

    private void ApplySample(PerformanceSample sample)
    {
        CpuPercent = sample.CpuPercent;
        CpuTemperatureText = sample.CpuTemperatureCelsius is { } cpuTemp ? $"{cpuTemp:F0} °C" : "—";
        CpuClockText = sample.CpuClockGHz > 0 ? $"{sample.CpuClockGHz:F2} GHz" : "—";

        MemoryPercent = sample.MemoryPercent;
        MemoryInUseText = ByteFormat.Format(sample.MemoryInUseBytes);
        MemoryAvailableText = ByteFormat.Format(sample.MemoryAvailableBytes);

        GpuPercent = sample.GpuPercent;
        GpuTemperatureText = sample.GpuTemperatureCelsius is { } gpuTemp ? $"{gpuTemp:F0} °C" : "—";
        GpuMemoryText = sample.GpuMemoryUsedBytes > 0 ? ByteFormat.Format(sample.GpuMemoryUsedBytes) : "—";

        DiskPercent = sample.DiskPercent;
        DiskReadText = RateFormat(sample.DiskReadBytesPerSecond);
        DiskWriteText = RateFormat(sample.DiskWriteBytesPerSecond);

        NetworkDownloadText = RateFormat(sample.NetworkDownloadBytesPerSecond);
        NetworkUploadText = RateFormat(sample.NetworkUploadBytesPerSecond);
        LatencyText = sample.NetworkLatencyMs is { } latency ? $"{latency:F0} ms" : "—";

        ProcessCount = sample.ProcessCount;
        ThreadCount = sample.ThreadCount;
        LastSampleText = sample.Timestamp.ToString("HH:mm:ss");

        var window = Math.Max(15, HistorySeconds / Math.Max(1, IntervalSeconds));

        Push(_cpuHistory, sample.CpuPercent, window);
        Push(_memoryHistory, sample.MemoryPercent, window);
        Push(_gpuHistory, sample.GpuPercent, window);
        Push(_diskReadHistory, ToMb(sample.DiskReadBytesPerSecond), window);
        Push(_diskWriteHistory, ToMb(sample.DiskWriteBytesPerSecond), window);
        Push(_downloadHistory, ToKb(sample.NetworkDownloadBytesPerSecond), window);
        Push(_uploadHistory, ToKb(sample.NetworkUploadBytesPerSecond), window);
        Push(_timeLabels, sample.Timestamp.ToString("HH:mm:ss"), window);

        _sampleCounter++;

        if (_sampleCounter % ProcessRefreshEvery == 0)
        {
            _ = LoadProcessesAsync();
        }
    }

    private void SeedHistory()
    {
        var history = _monitor.History;

        if (history.Count == 0)
        {
            return;
        }

        Clear(_cpuHistory);
        Clear(_memoryHistory);
        Clear(_gpuHistory);
        Clear(_diskReadHistory);
        Clear(_diskWriteHistory);
        Clear(_downloadHistory);
        Clear(_uploadHistory);
        Clear(_timeLabels);

        foreach (var sample in history)
        {
            ApplySample(sample);
        }
    }

    private async Task LoadStaticInfoAsync()
    {
        try
        {
            var profile = await _systemInformation.GetSystemProfileAsync().ConfigureAwait(true);

            OnUiThread(() =>
            {
                MemoryTotalText = profile.Memory.TotalFormatted;

                Partitions.Clear();

                foreach (var partition in profile.Partitions.Where(p => p.DriveType.Equals("Fixed", StringComparison.OrdinalIgnoreCase)))
                {
                    Partitions.Add(partition);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao carregar as informações estáticas de monitoramento.");
        }
    }

    private async Task LoadProcessesAsync()
    {
        try
        {
            var list = SortBy.Equals("memory", StringComparison.OrdinalIgnoreCase)
                ? await _processes.GetTopByMemoryAsync(60).ConfigureAwait(true)
                : await _processes.GetTopByCpuAsync(60).ConfigureAwait(true);

            OnUiThread(() =>
            {
                var selectedId = SelectedProcess?.Id;

                Processes.Clear();

                foreach (var process in list)
                {
                    Processes.Add(process);
                }

                SelectedProcess = Processes.FirstOrDefault(p => p.Id == selectedId);
            });
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Falha ao atualizar a lista de processos.");
        }
    }

    private static void Push<T>(ObservableCollection<T> collection, T value, int window)
    {
        collection.Add(value);

        while (collection.Count > window)
        {
            collection.RemoveAt(0);
        }
    }

    private static void Clear<T>(ObservableCollection<T> collection)
    {
        while (collection.Count > 0)
        {
            collection.RemoveAt(0);
        }
    }

    private static int ClampInterval(int seconds) => seconds switch
    {
        <= 1 => 1,
        <= 2 => 2,
        _ => 5
    };

    private static double ToMb(double bytesPerSecond) => bytesPerSecond / (1024d * 1024d);

    private static double ToKb(double bytesPerSecond) => bytesPerSecond / 1024d;

    private static string RateFormat(double bytesPerSecond) => $"{ByteFormat.Format((long)bytesPerSecond)}/s";
}

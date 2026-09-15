using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Application.Scoring;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Presentation.Controls;
using LiveChartsCore;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// Tela inicial: score de saúde, resumo do hardware, gráficos em tempo real,
/// principais processos, recomendações e ações rápidas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Coleta em camadas.</b> <see cref="InitializeAsync"/> faz três passadas:
/// (1) perfil de hardware + amostra de desempenho → já dá para pintar o score
/// preliminar; (2) análise completa em background → recomendações;
/// (3) verificação de atualização. Cada etapa tem <c>try/catch</c> próprio: se a WMI
/// falhar, a tela continua útil com o que já foi coletado.
/// </para>
/// <para>
/// <b>Tempo real.</b> Enquanto a tela está visível, um <c>PeriodicTimer</c> de 2s lê
/// amostras do <see cref="IPerformanceMonitor"/> e alimenta janelas deslizantes de 30
/// pontos (<see cref="ObservableCollection{T}"/>), que o LiveCharts2 anima sozinho.
/// </para>
/// </remarks>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private const int ChartWindow = 30;

    private readonly ISystemScoreCalculator _scoreCalculator;
    private readonly ISystemInformationService _systemInformation;
    private readonly IPerformanceMonitor _monitor;
    private readonly ISystemAnalyzer _analyzer;
    private readonly IProcessService _processes;
    private readonly IPrivacyService _privacy;
    private readonly IStartupManager _startupManager;
    private readonly IPowerPlanService _powerPlans;
    private readonly IGameModeService _gameMode;
    private readonly IOptimizationService _optimization;
    private readonly ICleanupService _cleanup;
    private readonly IUpdateService _updates;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IActionHistoryService _history;

    private readonly ObservableCollection<double> _cpuHistory = [];
    private readonly ObservableCollection<double> _memoryHistory = [];
    private readonly ObservableCollection<string> _timeLabels = [];
    private readonly ObservableCollection<double> _scoreValue = [0];
    private readonly ObservableCollection<double> _scoreRemainder = [100];

    private CancellationTokenSource? _tickerCts;
    private SystemProfile _profile = SystemProfile.Empty;
    private PerformanceSample? _lastSample;

    /// <summary>Cria o ViewModel do Dashboard.</summary>
    /// <param name="scoreCalculator">Calculadora do score.</param>
    /// <param name="systemInformation">Inventário de hardware.</param>
    /// <param name="monitor">Monitor de desempenho.</param>
    /// <param name="analyzer">Análise do sistema.</param>
    /// <param name="processes">Processos.</param>
    /// <param name="privacy">Privacidade.</param>
    /// <param name="startupManager">Inicialização.</param>
    /// <param name="powerPlans">Planos de energia.</param>
    /// <param name="gameMode">Modo Gamer.</param>
    /// <param name="optimization">Otimização.</param>
    /// <param name="cleanup">Limpeza.</param>
    /// <param name="updates">Atualizações do produto.</param>
    /// <param name="navigation">Navegação.</param>
    /// <param name="dialogs">Diálogos.</param>
    /// <param name="history">Histórico de ações (cartão "Ações recentes").</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public DashboardViewModel(
        ISystemScoreCalculator scoreCalculator,
        ISystemInformationService systemInformation,
        IPerformanceMonitor monitor,
        ISystemAnalyzer analyzer,
        IProcessService processes,
        IPrivacyService privacy,
        IStartupManager startupManager,
        IPowerPlanService powerPlans,
        IGameModeService gameMode,
        IOptimizationService optimization,
        ICleanupService cleanup,
        IUpdateService updates,
        INavigationService navigation,
        IDialogService dialogs,
        IActionHistoryService history,
        ILocalizationService localization,
        ILogger<DashboardViewModel> logger)
        : base(localization, logger)
    {
        _scoreCalculator = scoreCalculator;
        _systemInformation = systemInformation;
        _monitor = monitor;
        _analyzer = analyzer;
        _processes = processes;
        _privacy = privacy;
        _startupManager = startupManager;
        _powerPlans = powerPlans;
        _gameMode = gameMode;
        _optimization = optimization;
        _cleanup = cleanup;
        _updates = updates;
        _navigation = navigation;
        _dialogs = dialogs;
        _history = history;

        CpuSeries = [ChartTheme.Line(_cpuHistory, ChartTheme.Accent, L("Mon_Cpu"))];
        MemorySeries = [ChartTheme.Line(_memoryHistory, ChartTheme.Primary, L("Mon_Memory"))];
        TimeAxes = ChartTheme.TimeAxes(_timeLabels);
        PercentAxes = ChartTheme.PercentAxes();
        ScoreSeries = ChartTheme.Gauge(_scoreValue, _scoreRemainder, ChartTheme.Accent, innerRadius: 56, outerRadius: 74);

        IsGameModeActive = _gameMode.IsActive;
        _gameMode.StateChanged += OnGameModeStateChanged;
    }

    /// <summary>Séries do anel de score (gauge).</summary>
    public ISeries[] ScoreSeries { get; }

    /// <summary>Séries do gráfico de CPU.</summary>
    public ISeries[] CpuSeries { get; }

    /// <summary>Séries do gráfico de memória.</summary>
    public ISeries[] MemorySeries { get; }

    /// <summary>Eixos X (tempo).</summary>
    public Axis[] TimeAxes { get; }

    /// <summary>Eixos Y (percentual).</summary>
    public Axis[] PercentAxes { get; }

    /// <summary>Principais processos por CPU.</summary>
    public ObservableCollection<ProcessSnapshot> TopProcesses { get; } = [];

    /// <summary>Recomendações priorizadas (máximo 6).</summary>
    public ObservableCollection<AnalysisIssue> Recommendations { get; } = [];

    /// <summary>Fatores que reduziram o score.</summary>
    public ObservableCollection<string> ScoreFactors { get; } = [];

    /// <summary>Ações recentes (histórico persistido).</summary>
    public ObservableCollection<ActionRecord> RecentActions { get; } = [];

    /// <summary>Score geral [0-100].</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreProgress))]
    private int _overallScore;

    /// <summary>Score geral como fração [0-1] (para barras e anéis).</summary>
    public double ScoreProgress => OverallScore / 100d;

    /// <summary>Classificação textual do score.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Mantém o anel do gauge sincronizado com o score.</summary>
    partial void OnOverallScoreChanged(int value) => OnUiThread(() =>
    {
        _scoreValue[0] = Math.Clamp(value, 0, 100);
        _scoreRemainder[0] = Math.Max(0, 100 - value);
    });

    /// <summary>Score de desempenho.</summary>
    [ObservableProperty]
    private int _performanceScore;

    /// <summary>Score de estabilidade.</summary>
    [ObservableProperty]
    private int _stabilityScore;

    /// <summary>Score de segurança.</summary>
    [ObservableProperty]
    private int _securityScore;

    /// <summary>Score de limpeza.</summary>
    [ObservableProperty]
    private int _cleanlinessScore;

    /// <summary>Texto do processador.</summary>
    [ObservableProperty]
    private string _cpuText = string.Empty;

    /// <summary>Uso instantâneo de CPU (%).</summary>
    [ObservableProperty]
    private double _cpuPercent;

    /// <summary>Texto da memória.</summary>
    [ObservableProperty]
    private string _memoryText = string.Empty;

    /// <summary>Uso de memória (%).</summary>
    [ObservableProperty]
    private double _memoryPercent;

    /// <summary>Texto da GPU.</summary>
    [ObservableProperty]
    private string _gpuText = string.Empty;

    /// <summary>Uso de GPU (%).</summary>
    [ObservableProperty]
    private double _gpuPercent;

    /// <summary>Texto do disco do sistema.</summary>
    [ObservableProperty]
    private string _diskText = string.Empty;

    /// <summary>Uso do disco do sistema (%).</summary>
    [ObservableProperty]
    private double _diskPercent;

    /// <summary>Texto do sistema operacional.</summary>
    [ObservableProperty]
    private string _osText = string.Empty;

    /// <summary>Temperaturas (CPU/GPU).</summary>
    [ObservableProperty]
    private string _temperatureText = string.Empty;

    /// <summary>Indica se há recomendações.</summary>
    public bool HasRecommendations => Recommendations.Count > 0;

    /// <summary>Indica se há fatores de score.</summary>
    public bool HasScoreFactors => ScoreFactors.Count > 0;

    /// <summary>Indica se há processos listados.</summary>
    public bool HasProcesses => TopProcesses.Count > 0;

    /// <summary>Indica se há ações recentes.</summary>
    public bool HasRecentActions => RecentActions.Count > 0;

    /// <summary>Espaço recuperável da última análise.</summary>
    [ObservableProperty]
    private string _recoverableText = "0 MB";

    /// <summary>Momento da última análise.</summary>
    [ObservableProperty]
    private string _lastScanText = string.Empty;

    /// <summary>Indica se há atualização do produto disponível.</summary>
    [ObservableProperty]
    private bool _hasUpdateAvailable;

    /// <summary>Versão da atualização disponível.</summary>
    [ObservableProperty]
    private string _updateText = string.Empty;

    /// <summary>Indica se o Modo Gamer está ativo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameModeText))]
    private bool _isGameModeActive;

    /// <summary>Texto do cartão do Modo Gamer.</summary>
    public string GameModeText => IsGameModeActive ? L("Game_Active") : L("Game_Inactive");

    /// <summary>Carrega os dados iniciais da tela.</summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        await LoadHardwareAsync().ConfigureAwait(true);
        await RecalculateScoreAsync().ConfigureAwait(true);

        StartTicker();

        // Etapas independentes em paralelo: análise pesada, processos, atualização e histórico.
        await Task.WhenAll(
            LoadRecommendationsAsync(),
            LoadTopProcessesAsync(),
            CheckUpdateAsync(),
            LoadRecentActionsAsync()).ConfigureAwait(true);

        LastScanText = LF("Dash_LastScan", DateTime.Now.ToString("HH:mm"));
    }

    /// <summary>Atualiza tudo manualmente.</summary>
    [RelayCommand]
    private async Task RefreshAsync() => await InitializeAsync().ConfigureAwait(true);

    /// <summary>Ação rápida: otimização expressa.</summary>
    [RelayCommand]
    private Task RunQuickOptimizationAsync() => RunOptimizationAsync(OptimizationOptions.ForQuick());

    /// <summary>Ação rápida: otimização completa.</summary>
    [RelayCommand]
    private Task RunFullOptimizationAsync() => RunOptimizationAsync(OptimizationOptions.ForFull());

    /// <summary>Ação rápida: limpeza segura.</summary>
    [RelayCommand]
    private async Task RunQuickCleanAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _cleanup.QuickCleanAsync().ConfigureAwait(true);

            StatusMessage = LF("Cleanup_ResultSummary", result.DeletedFileCount, result.FreedFormatted);

            await RecalculateScoreAsync().ConfigureAwait(true);
        }, L("Cleanup_Running")).ConfigureAwait(true);
    }

    /// <summary>Ação rápida: ativa/desativa o Modo Gamer.</summary>
    [RelayCommand]
    private async Task ToggleGameModeAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (_gameMode.IsActive)
            {
                await _gameMode.DeactivateAsync().ConfigureAwait(true);
            }
            else
            {
                var tweaks = _gameMode.AvailableTweaks.Where(t => t.IsEnabled).Select(t => t.Id).ToList();

                await _gameMode.ActivateAsync(tweaks).ConfigureAwait(true);
            }

            IsGameModeActive = _gameMode.IsActive;

            await RecalculateScoreAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Aplica a correção de uma recomendação específica.</summary>
    /// <param name="issue">Problema selecionado.</param>
    [RelayCommand]
    private async Task FixIssueAsync(AnalysisIssue? issue)
    {
        if (issue is null)
        {
            return;
        }

        if (!issue.CanAutoFix)
        {
            await _dialogs.ShowInfoAsync(L("Common_Details"), issue.RecommendedAction).ConfigureAwait(true);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _analyzer.FixAsync([issue]).ConfigureAwait(true);

            await _dialogs.ShowOptimizationResultAsync(result).ConfigureAwait(true);

            await LoadRecommendationsAsync().ConfigureAwait(true);
            await RecalculateScoreAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Navega para outra tela.</summary>
    /// <param name="navigationKey">Chave da tela.</param>
    [RelayCommand]
    private void Navigate(string navigationKey) => _navigation.NavigateTo(navigationKey);

    /// <summary>Instala a atualização disponível.</summary>
    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _updates.CheckAsync().ConfigureAwait(true);

            if (!result.IsAvailable)
            {
                HasUpdateAvailable = false;
                return;
            }

            await _updates.InstallAsync(result).ConfigureAwait(true);
        }, L("Settings_CheckingUpdate")).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom()
    {
        _tickerCts?.Cancel();
        _tickerCts?.Dispose();
        _tickerCts = null;
    }

    // ---------------------------------------------------------------------
    // Carga de dados
    // ---------------------------------------------------------------------

    private async Task LoadHardwareAsync()
    {
        try
        {
            _profile = await _systemInformation.GetSystemProfileAsync().ConfigureAwait(true);

            CpuText = _profile.Cpu.ShortDescription;
            MemoryText = $"{_profile.Memory.InUseFormatted} / {_profile.Memory.TotalFormatted}";
            GpuText = _profile.PrimaryGpu?.Name ?? L("Common_NotAvailable");
            OsText = _profile.OperatingSystem.ShortDescription;

            var partition = _profile.SystemPartition;

            if (partition is not null)
            {
                DiskText = $"{partition.DeviceId} · {ByteFormat.Format(partition.FreeBytes)} {L("Common_Free")} / {ByteFormat.Format(partition.SizeBytes)}";
                DiskPercent = partition.UsedPercent;
            }

            _lastSample = await _monitor.ReadSampleAsync().ConfigureAwait(true);

            ApplySample(_lastSample);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao carregar o hardware no Dashboard.");

            CpuText = L("Common_NotAvailable");
            MemoryText = L("Common_NotAvailable");
            GpuText = L("Common_NotAvailable");
            OsText = L("Common_NotAvailable");
            DiskText = L("Common_NotAvailable");
        }
    }

    private async Task LoadRecommendationsAsync()
    {
        try
        {
            var report = await _analyzer.AnalyzeAsync().ConfigureAwait(true);

            Recommendations.Clear();

            foreach (var issue in report.Issues.OrderByDescending(i => i.Severity).ThenByDescending(i => i.RecoverableBytes).Take(6))
            {
                Recommendations.Add(issue);
            }

            RecoverableText = report.TotalRecoverableFormatted;

            OnPropertyChanged(nameof(HasRecommendations));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao carregar as recomendações do Dashboard.");
        }
    }

    private async Task LoadTopProcessesAsync()
    {
        try
        {
            var top = await _processes.GetTopByCpuAsync(6).ConfigureAwait(true);

            TopProcesses.Clear();

            foreach (var process in top)
            {
                TopProcesses.Add(process);
            }

            OnPropertyChanged(nameof(HasProcesses));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao listar os processos no Dashboard.");
        }
    }

    /// <summary>Carrega as últimas ações registradas (cartão "Ações recentes").</summary>
    private async Task LoadRecentActionsAsync()
    {
        try
        {
            var actions = await _history.GetRecentAsync(6).ConfigureAwait(true);

            OnUiThread(() =>
            {
                RecentActions.Clear();

                foreach (var action in actions)
                {
                    RecentActions.Add(action);
                }

                OnPropertyChanged(nameof(HasRecentActions));
            });
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Falha ao carregar as ações recentes.");
        }
    }

    private async Task CheckUpdateAsync()
    {
        try
        {
            var result = await _updates.CheckAsync().ConfigureAwait(true);

            HasUpdateAvailable = result.IsAvailable;

            if (result.IsAvailable)
            {
                UpdateText = LF("Settings_UpdateAvailableShort", result.LatestVersion);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Verificação de atualização falhou (offline?).");
        }
    }

    /// <summary>Recalcula o score combinando todas as fontes disponíveis.</summary>
    private async Task RecalculateScoreAsync()
    {
        try
        {
            var privacyItems = await _privacy.GetItemsAsync().ConfigureAwait(true);
            var startupPrograms = await _startupManager.GetStartupProgramsAsync().ConfigureAwait(true);
            var activePlan = await _powerPlans.GetActivePlanAsync().ConfigureAwait(true);
            var antivirus = await _systemInformation.IsAntivirusActiveAsync().ConfigureAwait(true);
            var firewall = await _systemInformation.IsFirewallActiveAsync().ConfigureAwait(true);
            var uac = await _systemInformation.IsUacEnabledAsync().ConfigureAwait(true);

            var input = ScoreInputBuilder.Build(
                _profile,
                _lastSample,
                report: null,
                privacyItems: privacyItems,
                startupPrograms: startupPrograms,
                gameMode: null,
                activePowerPlan: activePlan,
                antivirusActive: antivirus,
                firewallActive: firewall,
                uacEnabled: uac);

            var score = _scoreCalculator.Calculate(input);

            ApplyScore(score);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao recalcular o score do sistema.");

            StatusMessage = L("Msg_NeedAdmin");
        }
    }

    private void ApplyScore(SystemScoreResult score)
    {
        OverallScore = score.OverallScore;
        PerformanceScore = score.PerformanceScore;
        StabilityScore = score.StabilityScore;
        SecurityScore = score.SecurityScore;
        CleanlinessScore = score.CleanlinessScore;
        StatusText = Localization.GetEnumText(score.Status);

        ScoreFactors.Clear();

        foreach (var factor in score.Factors.Take(5))
        {
            ScoreFactors.Add(factor);
        }

        OnPropertyChanged(nameof(HasScoreFactors));
    }

    private void ApplySample(PerformanceSample? sample)
    {
        if (sample is null)
        {
            return;
        }

        CpuPercent = sample.CpuPercent;
        MemoryPercent = sample.MemoryPercent;
        GpuPercent = sample.GpuPercent;

        TemperatureText = BuildTemperatureText(sample);
    }

    private static string BuildTemperatureText(PerformanceSample sample)
    {
        var parts = new List<string>(2);

        if (sample.CpuTemperatureCelsius is { } cpu)
        {
            parts.Add($"CPU {cpu:F0}°C");
        }

        if (sample.GpuTemperatureCelsius is { } gpu)
        {
            parts.Add($"GPU {gpu:F0}°C");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "—";
    }

    /// <summary>Executa uma otimização com diálogo de progresso e resultado.</summary>
    private async Task RunOptimizationAsync(OptimizationOptions options)
    {
        OptimizationResult? result = null;

        var completed = await _dialogs.ShowProgressAsync(
            L("Opt_Running"),
            async (progress, cancellationToken) =>
            {
                result = await _optimization.OptimizeAsync(options, progress, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (completed && result is not null)
        {
            await _dialogs.ShowOptimizationResultAsync(result).ConfigureAwait(true);

            await LoadHardwareAsync().ConfigureAwait(true);
            await RecalculateScoreAsync().ConfigureAwait(true);
            await LoadRecommendationsAsync().ConfigureAwait(true);
        }
        else
        {
            StatusMessage = L("Dlg_CancelledTitle");
        }
    }

    // ---------------------------------------------------------------------
    // Tempo real
    // ---------------------------------------------------------------------

    /// <summary>Inicia o ticker de 2s que alimenta os gráficos.</summary>
    private void StartTicker()
    {
        _tickerCts?.Cancel();
        _tickerCts?.Dispose();
        _tickerCts = new CancellationTokenSource();

        var token = _tickerCts.Token;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var sample = await _monitor.ReadSampleAsync(token).ConfigureAwait(false);

                    OnUiThread(() =>
                    {
                        ApplySample(sample);
                        Push(_cpuHistory, sample.CpuPercent);
                        Push(_memoryHistory, sample.MemoryPercent);
                        Push(_timeLabels, sample.Timestamp.ToString("HH:mm:ss"));
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Saída normal ao navegar para outra tela.
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Ticker de desempenho do Dashboard interrompido.");
            }
        }, token);
    }

    /// <summary>Adiciona um ponto à janela deslizante.</summary>
    private static void Push<T>(ObservableCollection<T> collection, T value)
    {
        collection.Add(value);

        while (collection.Count > ChartWindow)
        {
            collection.RemoveAt(0);
        }
    }

    private void OnGameModeStateChanged(object? sender, GameModeState state) =>
        OnUiThread(() => IsGameModeActive = state.IsActive);
}

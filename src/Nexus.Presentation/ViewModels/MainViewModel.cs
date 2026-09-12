using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexus.Application;
using Nexus.Application.Optimization;
using Nexus.Application.Telemetry;
using Nexus.Application.UseCases;
using Nexus.Domain.Metrics;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;
using Nexus.Presentation.Converters;
using Nexus.Presentation.Notifications;

namespace Nexus.Presentation.ViewModels;

/// <summary>
/// Item de navegação da sidebar (spec §3.3). <see cref="PatchRef"/> null =
/// módulo já funcional neste patch.
/// </summary>
public sealed record NavItem(
    string Key,
    string Title,
    string Glyph,
    string Description,
    FeatureVisibility Visibility,
    string? PatchRef);

/// <summary>
/// ViewModel da janela principal: navegação (Modo Simples/Avançado),
/// indicador de score, elevação, Modo Desempenho Máximo.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IElevationService _elevation;
    private readonly TelemetryEvents _telemetry;
    private readonly INotificationHub _notifications;
    private readonly RunOptimizationUseCase _runOptimization;
    private readonly IOptimizationTaskCatalog _catalog;
    private readonly DashboardViewModel _dashboard;
    private readonly ILogger<MainViewModel> _log;

    private NavItem? _selectedNav;
    private object? _currentView;
    private double? _score;
    private string _scoreText = MetricValue.NotAvailable;
    private string _scoreBand = "—";
    private string _scoreNote = string.Empty;
    private Brush _scoreBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x93, 0xB0));
    private bool _isAdvanced;
    private bool _isMaxPerformance;
    private bool _initialized;

    public MainViewModel(
        NotificationCenter notifications,
        ISettingsService settings,
        IElevationService elevation,
        TelemetryEvents telemetry,
        DashboardViewModel dashboard,
        RunOptimizationUseCase runOptimization,
        IOptimizationTaskCatalog catalog,
        INotificationHub notificationHub,
        ILogger<MainViewModel> log)
    {
        Notifications = notifications;
        _settings = settings;
        _elevation = elevation;
        _telemetry = telemetry;
        _dashboard = dashboard;
        _runOptimization = runOptimization;
        _catalog = catalog;
        _notifications = notificationHub;
        _log = log;

        NavItems = BuildNavItems();
        IsElevated = elevation.IsElevated;

        RunAsAdminCommand = new AsyncRelayCommand(RequestElevationAsync);

        _telemetry.SampleRecorded += OnTelemetrySample;

        SelectedNav = NavItems.FirstOrDefault(n => n.Visibility == FeatureVisibility.Simple)
                      ?? NavItems[0];
        _ = InitializeAsync();
    }

    public NotificationCenter Notifications { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    /// <summary>
    /// Navegação filtrada pelo modo (spec §7): o Modo Simples esconde
    /// TUDO o que é marcado como Avançado.
    /// </summary>
    public IEnumerable<NavItem> VisibleNavItems =>
        NavItems.Where(n => ModeFilter.IsVisible(n.Visibility, IsAdvanced)).ToList();

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (Set(ref _selectedNav, value))
            {
                CurrentView = value is null
                    ? null
                    : value.Key == "dashboard" ? _dashboard : new PlaceholderViewModel(value);
            }
        }
    }

    public object? CurrentView
    {
        get => _currentView;
        private set => Set(ref _currentView, value);
    }

    public IAsyncRelayCommand RunAsAdminCommand { get; }

    /// <summary>Score do chip do cabeçalho (atualizado pela telemetria a cada 5 s).</summary>
    public double? Score
    {
        get => _score;
        private set
        {
            if (!Set(ref _score, value))
                return;
            ScoreText = value is null ? MetricValue.NotAvailable : value.Value.ToString("0");
            ScoreBand = ScoreBands.BandText(value);
            ScoreBrush = ScoreBands.BrushFor(value);
        }
    }

    public string ScoreText { get => _scoreText; private set => Set(ref _scoreText, value); }
    public string ScoreBand { get => _scoreBand; private set => Set(ref _scoreBand, value); }
    public string ScoreNote { get => _scoreNote; private set => Set(ref _scoreNote, value); }
    public Brush ScoreBrush { get => _scoreBrush; private set => Set(ref _scoreBrush, value); }

    public bool IsAdvanced
    {
        get => _isAdvanced;
        set
        {
            if (Set(ref _isAdvanced, value))
            {
                OnPropertyChanged(nameof(VisibleNavItems));

                // Se o item selecionado ficou oculto no novo modo, volta ao primeiro visível.
                if (SelectedNav is { } current && !ModeFilter.IsVisible(current.Visibility, value))
                    SelectedNav = VisibleNavItems.FirstOrDefault();

                _ = PersistModeAsync();
                _dashboard.OnModeChanged();
            }
        }
    }

    /// <summary>Modo Desempenho Máximo (header global): aplica todas as tarefas seguras registadas.</summary>
    public bool IsMaxPerformance
    {
        get => _isMaxPerformance;
        set
        {
            if (Set(ref _isMaxPerformance, value) && value)
                _ = RunMaxPerformanceAsync();
        }
    }

    public bool IsElevated { get; }

    public string ElevationButtonText => IsElevated ? "✓ Administrador" : "Executar como Admin";

    public string ElevationHint => IsElevated
        ? "A app está a correr elevada."
        : "Modo limitado: a app não está elevada. Clique para relançar com privilégios (UAC).";

    private static IReadOnlyList<NavItem> BuildNavItems() => new List<NavItem>
    {
        new("dashboard",
            "Painel Principal",
            "\uE80F",
            "Score de desempenho, métricas reais e recomendações.",
            FeatureVisibility.Simple,
            null),

        new("quick-optimization",
            "Otimização Rápida",
            "\uE945",
            "Conjunto de tweaks seguros de baixo risco, aplicáveis num clique, com benchmark A/B medido " +
            "antes/depois e rollback individual. Parte do pipeline já existe neste patch (a tarefa de " +
            "telemetria está ativa nas Recomendações); o benchmark A/B chega no patch seguinte.",
            FeatureVisibility.Simple,
            "patch 2"),

        new("advanced-cleanup",
            "Limpeza Avançada",
            "\uE74D",
            "Varrimento real de caches, logs, lixeira e atualizações órfãs com PREVIEW obrigatório antes " +
            "de qualquer eliminação — nada é apagado sem confirmação explícita.",
            FeatureVisibility.Advanced,
            "patch 4"),

        new("monitor",
            "Desempenho / Monitor",
            "\uE9D2",
            "Métricas ao vivo (CPU, RAM, GPU/VRAM, temperatura, rede, processos) com N/D honesto onde " +
            "não há fonte real. O Painel Principal já expõe CPU, RAM, disco, temperatura, processos e uptime.",
            FeatureVisibility.Simple,
            "patch 5"),

        new("startup",
            "Inicialização",
            "\uE7E8",
            "Gestão de programas de arranque (registry Run/RunOnce, pasta Startup, Task Scheduler) com " +
            "heurística de impacto baseada em catálogo real — não em suposição genérica.",
            FeatureVisibility.Advanced,
            "patch 4"),

        new("services",
            "Serviços",
            "\uE950",
            "Gestão real de serviços Windows (WMI + sc.exe) dentro do CommandExecutor com whitelist, com " +
            "perfis de otimização (Gaming, Privacidade) e preview de impacto antes de aplicar.",
            FeatureVisibility.Advanced,
            "patch 2"),

        new("privacy",
            "Privacidade / Telemetria",
            "\uE72E",
            "Toggles reais sobre chaves/serviços de telemetria conhecidos, cada um documentado. A " +
            "desativação da telemetria de base já está implementada neste patch (ver Recomendações).",
            FeatureVisibility.Simple,
            "patch 2"),

        new("network",
            "Rede",
            "\uE701",
            "Internet Booster: gestão real de adaptadores/DNS com medição antes/depois. Sem 'boost' " +
            "mágico — apenas o que se pode medir e reverter.",
            FeatureVisibility.Advanced,
            "patch 6"),

        new("tools",
            "Ferramentas Rápidas",
            "\uE90F",
            "Grid de ações one-click (limpeza, telemetria, reparação do sistema) com feedback tipado e " +
            "registo no histórico. As ações reais entram com os seus módulos.",
            FeatureVisibility.Simple,
            "patch 3"),

        new("personalization",
            "Personalização",
            "\uE2B1",
            "Tema, densidade da interface, fontes e atalhos.",
            FeatureVisibility.Advanced,
            "patch 6"),

        new("reports",
            "Relatórios / Histórico",
            "\uE7C3",
            "Timeline pesquisável de todas as ações com timestamp, resultado e rollback individual; " +
            "exportação PDF/CSV gerada a partir da base SQLite (sem dados fictícios).",
            FeatureVisibility.Advanced,
            "patch 7"),

        new("settings",
            "Configurações",
            "\uE713",
            "Modo Simples/Avançado, janela do gráfico, auto-dismiss de notificações e gestão de " +
            "backups/rollback.",
            FeatureVisibility.Simple,
            "patch 6"),
    };

    private async Task InitializeAsync()
    {
        if (_initialized)
            return;
        _initialized = true;

        try
        {
            var settings = await _settings.LoadAsync();
            IsAdvanced = settings.AdvancedMode;

            if (!IsElevated)
            {
                await _notifications.EmitAsync(
                    NotificationKind.Warning,
                    "Modo limitado (sem administrador)",
                    "As alterações de sistema (registry/serviços) estão bloqueadas. Use 'Executar como " +
                    "Admin' no cabeçalho quando pretender aplicar otimizações.");
            }

            await _dashboard.RefreshAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha na inicialização do MainViewModel.");
        }
    }

    private async Task PersistModeAsync()
    {
        try
        {
            var s = await _settings.LoadAsync();
            s.AdvancedMode = IsAdvanced;
            await _settings.SaveAsync(s);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Não foi possível guardar o modo.");
        }
    }

    private void OnTelemetrySample(object? sender, TelemetryEvents.SampleEvent e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        dispatcher.BeginInvoke(() =>
        {
            Score = e.Score.Score;
            ScoreNote = e.Score.Summary;
        });
    }

    private async Task RunMaxPerformanceAsync()
    {
        var candidates = _catalog.All
            .Where(t => t.Risk == RiskLevel.Low && t.Visibility == FeatureVisibility.Simple)
            .ToList();

        if (candidates.Count == 0)
        {
            await _notifications.EmitAsync(
                NotificationKind.Info,
                "Modo Desempenho Máximo",
                "Ainda não existem tarefas seguras registadas nesta versão — verifique o roadmap (docs/patches).");
            IsMaxPerformance = false;
            return;
        }

        foreach (var task in candidates)
        {
            // O orquestrador emite a notificação tipada de cada desfecho.
            await _runOptimization.ExecuteAsync(task.Key);
        }

        IsMaxPerformance = false;
    }

    private async Task RequestElevationAsync()
    {
        if (IsElevated)
            return;

        bool started = await _elevation.RequestElevationAsync();
        if (started)
        {
            await _notifications.EmitAsync(
                NotificationKind.Info,
                "Reinício como administrador",
                "Confirme o prompt UAC — a nova instância abre elevada. Esta instância vai encerrar.");
            Application.Current?.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        }
        else
        {
            await _notifications.EmitAsync(
                NotificationKind.Warning,
                "Elevação cancelada",
                "O prompt UAC foi fechado. A app mantém-se em modo limitado (ações de sistema bloqueadas).");
        }
    }
}

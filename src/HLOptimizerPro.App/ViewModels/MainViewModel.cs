using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using HLOptimizerPro.Core.Services.Tweaks;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>Item de navegação da barra lateral (pode ter submenu, como "Ferramentas").</summary>
public sealed partial class NavigationItem : ObservableObject
{
    public NavigationItem(string key, string label, string iconKind, bool isChild = false)
    {
        Key = key;
        Label = label;
        IconKind = iconKind;
        IsChild = isChild;
    }

    public string Key { get; }
    public string Label { get; }
    public string IconKind { get; }
    public bool IsChild { get; }

    /// <summary>Itens filhos (submenu). Vazio nos itens de topo.</summary>
    public ObservableCollection<NavigationItem> Children { get; } = new();

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;

    public bool HasChildren => Children.Count > 0;
}

/// <summary>
/// ViewModel do shell da aplicação: barra lateral, barra de título personalizada, rodapé de estado
/// e anfitrião das páginas.
///
/// Mantém o relógio do cabeçalho (hora/data do Dashboard), o estado do sistema ("Sistema otimizado")
/// e as notificações. É o único ViewModel que conhece o serviço de navegação.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly PrivilegeService _privileges;
    private readonly AccountService _accounts;
    private readonly LicenseService _license;
    private readonly GameModeService _gameMode;
    private readonly OptimizationCatalog _catalog;
    private readonly DispatcherTimer _clockTimer;

    public MainViewModel(
        NotificationService notifications,
        INavigationService navigation,
        IDialogService dialogs,
        SettingsService settings,
        PrivilegeService privileges,
        AccountService accounts,
        LicenseService license,
        GameModeService gameMode,
        OptimizationCatalog catalog,
        ShellStateService shell)
        : base(notifications)
    {
        _navigation = navigation;
        _dialogs = dialogs;
        _settings = settings;
        _privileges = privileges;
        _accounts = accounts;
        _license = license;
        _gameMode = gameMode;
        _catalog = catalog;
        Shell = shell;

        BuildNavigation();

        _navigation.Navigated += (_, viewModel) => CurrentPage = viewModel;
        notifications.NotificationRaised += (_, notification) => AddToast(notification);

        // Relógio do cabeçalho: uma vez por segundo é suficiente e é praticamente sem custo.
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        UpdateClock();
    }

    // ---- Estado do shell ------------------------------------------------------
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private string _clockText = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _userProfile = string.Empty;

    [ObservableProperty]
    private bool _isSimulationMode;

    /// <summary>Evita notificações durante a leitura inicial das preferências.</summary>
    private bool _suppressSimulationHandler;

    /// <summary>Notificações visíveis (toasts), máximo de 4 simultâneas.</summary>
    public ObservableCollection<AppNotification> Toasts { get; } = new();

    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    public string ProductName => AppConstants.ProductName;
    public string VersionText => $"v{AppConstants.Version}";
    public bool IsElevated => _privileges.IsElevated;

    /// <summary>Estado partilhado do shell (estado do sistema, Modo Jogo, pendentes).</summary>
    public ShellStateService Shell { get; }

    /// <summary>Iniciais mostradas no avatar da barra lateral (ex.: "HL").</summary>
    public string Initials => _accounts.Current.AvatarInitials;

    /// <summary>Texto da licença apresentado no rodapé da barra lateral.</summary>
    public string LicenseText => _license.Current.IsPremium ? "Premium" : "Free";

    /// <summary>Estado de elevação apresentado no rodapé (aviso explícito quando não elevado).</summary>
    public string PrivilegeText => _privileges.IsElevated
        ? "Administrador"
        : "Sem privilégios de administrador";

    public HealthLevel PrivilegeLevel => _privileges.IsElevated ? HealthLevel.Otimo : HealthLevel.Atencao;

    // ---- Navegação ------------------------------------------------------------

    /// <summary>Cria a estrutura da barra lateral: itens de topo + submenu de Ferramentas.</summary>
    private void BuildNavigation()
    {
        var tools = new NavigationItem(NavigationKeys.PcBoost, "Ferramentas", "Tools");
        tools.Children.Add(new NavigationItem(NavigationKeys.Memory, "Memória RAM", "Memory", isChild: true));
        tools.Children.Add(new NavigationItem(NavigationKeys.Network, "Rede", "LanConnect", isChild: true));
        tools.Children.Add(new NavigationItem(NavigationKeys.AppTuner, "App Tuner", "Apps", isChild: true));
        tools.Children.Add(new NavigationItem(NavigationKeys.Tweaks, "Tweaks", "Tune", isChild: true));
        tools.Children.Add(new NavigationItem(NavigationKeys.GameMode, "Modo Jogo", "ControllerClassic", isChild: true));
        tools.Children.Add(new NavigationItem(NavigationKeys.Latency, "Latência", "TimerOutline", isChild: true));

        NavigationItems.Add(new NavigationItem(NavigationKeys.Home, "Início", "ViewDashboard"));
        NavigationItems.Add(new NavigationItem(NavigationKeys.Assessment, "Avaliação", "ChartBox"));
        NavigationItems.Add(tools);
        NavigationItems.Add(new NavigationItem(NavigationKeys.Optimizations, "Otimizações", "RocketLaunch"));
        NavigationItems.Add(new NavigationItem(NavigationKeys.Recommendations, "Recomendações", "LightbulbOn"));
        NavigationItems.Add(new NavigationItem(NavigationKeys.Settings, "Configuração", "Cog"));
        NavigationItems.Add(new NavigationItem(NavigationKeys.Help, "Ajuda", "HelpCircle"));
        NavigationItems.Add(new NavigationItem(NavigationKeys.About, "Sobre / Premium", "Information"));
    }

    /// <summary>Navega para a chave indicada (usado pelos itens da sidebar e pelos atalhos).</summary>
    [RelayCommand]
    private async Task NavigateAsync(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        await _navigation.NavigateToAsync(key);

        // Atualiza o estado visual dos itens (seleção, expansão do menu Ferramentas).
        foreach (var item in NavigationItems)
        {
            item.IsSelected = string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase);

            foreach (var child in item.Children)
                child.IsSelected = string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase);

            if (item.HasChildren)
                item.IsExpanded = item.IsExpanded || item.Children.Any(c => c.IsSelected);
        }
    }

    [RelayCommand]
    private void ToggleToolsExpansion(NavigationItem? item)
    {
        if (item is null || !item.HasChildren) return;
        item.IsExpanded = !item.IsExpanded;
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await _navigation.GoBackAsync();
    }

    // ---- Modo Simular ---------------------------------------------------------

    /// <summary>Alterna o modo Simular (usado também pelo comutador da barra de título).</summary>
    [RelayCommand]
    private void ToggleSimulation() => IsSimulationMode = !IsSimulationMode;

    /// <summary>
    /// Reage à mudança do modo Simular: persiste a preferência, avisa o motor de otimizações e
    /// informa o utilizador. Enquanto está ativo, nada é escrito no sistema (requisito de segurança).
    /// </summary>
    partial void OnIsSimulationModeChanged(bool value)
    {
        if (_suppressSimulationHandler) return;

        _settings.Update(s => s.SimulationMode = value);
        _catalog.SimulationMode = value;   // o catálogo descreve em vez de aplicar

        if (value)
            Notifications.Warn("Modo Simular", "Nada será alterado no sistema. As otimizações serão apenas descritas.");
        else
            Notifications.Info("Modo Simular desligado", "As otimizações voltam a ser aplicadas a sério.");
    }

    /// <summary>Confirma e aplica uma alteração com confirmação ao nível do shell.</summary>
    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continuar", bool danger = false)
        => _dialogs.ConfirmAsync(title, message, confirmText, danger);

    // ---- Estado / notificações ------------------------------------------------

    /// <summary>Atualiza o relógio do cabeçalho (hora e data em português).</summary>
    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText = Formatters.ShortTime(now);
        DateText = Formatters.LongDate(now);

        UserName = string.IsNullOrWhiteSpace(_accounts.Current.DisplayName) ? "Utilizador" : _accounts.Current.DisplayName;
        UserProfile = _accounts.Current.ProfileText;
    }

    /// <summary>Atualiza o indicador do Modo Jogo (chamado pela página Modo Jogo).</summary>
    public void UpdateGameMode(bool active) => Shell.IsGameModeActive = active;

    private void AddToast(AppNotification notification)
    {
        Toasts.Insert(0, notification);

        while (Toasts.Count > 4)
            Toasts.RemoveAt(Toasts.Count - 1);

        // Remove automaticamente passados 6 segundos (sem bloquear a interação).
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(notification);
        };
        timer.Start();
    }

    [RelayCommand]
    private void DismissToast(AppNotification? notification)
    {
        if (notification is not null) Toasts.Remove(notification);
    }

    [RelayCommand]
    private void CloseApplication()
    {
        if (System.Windows.Application.Current is App app)
            app.RequestShutdown();
    }

    public override async Task OnNavigatedToAsync(object? parameter = null)
    {
        await base.OnNavigatedToAsync(parameter);

        _suppressSimulationHandler = true;
        IsSimulationMode = _settings.Current.SimulationMode;
        _catalog.SimulationMode = IsSimulationMode;
        _suppressSimulationHandler = false;

        Shell.IsGameModeActive = _gameMode.Status.State == GameModeState.Ativo;
        Shell.PendingOptimizations = _catalog.AvailableCount;

        if (_navigation.Current is null)
            await NavigateAsync(NavigationKeys.Home);
    }

    /// <summary>Liberta o relógio quando o shell é fechado.</summary>
    public void StopTimers() => _clockTimer.Stop();
}

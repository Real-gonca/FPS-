using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// ViewModel da janela principal (shell): cabeçalho com o resumo do hardware,
/// selo de privilégio, navegação entre as telas e ações globais.
/// </summary>
/// <remarks>
/// <para>
/// <b>Navegação desacoplada.</b> A shell só conhece chaves
/// (<see cref="NavigationKeys"/>); quem instancia os ViewModels é o
/// <see cref="NavigationService"/>. O <c>ContentControl</c> da MainWindow exibe
/// <see cref="CurrentViewModel"/> e os DataTemplates de <c>ViewTemplates.xaml</c>
/// fazem o pareamento ViewModel → View.
/// </para>
/// <para>
/// <b>Elevação sob demanda.</b> O manifesto é <c>asInvoker</c>: o selo mostra se o
/// processo é administrador e o botão "Elevar" reinicia o aplicativo com UAC quando
/// o usuário quer executar tudo com privilégios.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly ISystemInformationService _systemInformation;
    private readonly IElevationService _elevation;
    private readonly IGameModeService _gameMode;
    private readonly IUpdateService _updates;
    private readonly ILogger<MainViewModel> _logger;

    private CancellationTokenSource? _uptimeCts;

    /// <summary>Cria o ViewModel da shell.</summary>
    /// <param name="navigation">Serviço de navegação.</param>
    /// <param name="systemInformation">Inventário do hardware.</param>
    /// <param name="elevation">Elevação sob demanda.</param>
    /// <param name="gameMode">Modo Gamer (toggle do cabeçalho).</param>
    /// <param name="updates">Versão do produto.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public MainViewModel(
        INavigationService navigation,
        ISystemInformationService systemInformation,
        IElevationService elevation,
        IGameModeService gameMode,
        IUpdateService updates,
        ILocalizationService localization,
        ILogger<MainViewModel> logger)
        : base(localization, logger)
    {
        _navigation = navigation;
        _systemInformation = systemInformation;
        _elevation = elevation;
        _gameMode = gameMode;
        _updates = updates;
        _logger = logger;

        AppTitle = L("App_Title");
        AppTagline = L("App_Tagline");
        VersionText = _updates.CurrentVersion;
        IsAdmin = _elevation.IsElevated;

        _navigation.Navigated += OnNavigated;
        _gameMode.StateChanged += OnGameModeStateChanged;
    }

    /// <summary>Título do produto.</summary>
    public string AppTitle { get; }

    /// <summary>Slogan exibido no cabeçalho.</summary>
    public string AppTagline { get; }

    /// <summary>Versão do produto.</summary>
    public string VersionText { get; }

    /// <summary>ViewModel da tela atual (bind do ContentControl).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentKey))]
    private object? _currentViewModel;

    /// <summary>Chave da tela atual (usada para marcar o item do menu).</summary>
    public string? CurrentKey => _navigation.CurrentKey;

    /// <summary>Indica se o processo é administrador.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrivilegeBadgeText))]
    [NotifyPropertyChangedFor(nameof(IsStandardUser))]
    private bool _isAdmin;

    /// <summary>Texto do selo de privilégio.</summary>
    public string PrivilegeBadgeText => IsAdmin ? L("App_AdminBadge") : L("App_UserBadge");

    /// <summary>True quando roda sem administrador (mostra o botão "Elevar").</summary>
    public bool IsStandardUser => !IsAdmin;

    /// <summary>Nome do computador e do usuário.</summary>
    [ObservableProperty]
    private string _machineText = string.Empty;

    /// <summary>Resumo do processador.</summary>
    [ObservableProperty]
    private string _cpuText = string.Empty;

    /// <summary>Resumo da memória.</summary>
    [ObservableProperty]
    private string _memoryText = string.Empty;

    /// <summary>Resumo da GPU.</summary>
    [ObservableProperty]
    private string _gpuText = string.Empty;

    /// <summary>Resumo do sistema operacional.</summary>
    [ObservableProperty]
    private string _osText = string.Empty;

    /// <summary>Tempo ligado (atualizado a cada minuto).</summary>
    [ObservableProperty]
    private string _uptimeText = string.Empty;

    /// <summary>Indica se o Modo Gamer está ativo (toggle do cabeçalho).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameModeButtonText))]
    private bool _isGameModeActive;

    /// <summary>Texto do botão do Modo Gamer.</summary>
    public string GameModeButtonText => IsGameModeActive ? L("Game_Deactivate") : L("Game_Activate");

    /// <summary>Pode voltar para a tela anterior.</summary>
    public bool CanGoBack => _navigation.CanGoBack;

    /// <summary>Carrega o resumo do hardware (chamado no startup).</summary>
    [RelayCommand]
    private async Task LoadSummaryAsync()
    {
        MachineText = $"{Environment.MachineName} · {Environment.UserName}";

        try
        {
            var profile = await _systemInformation.GetSystemProfileAsync().ConfigureAwait(true);

            ApplyProfile(profile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao carregar o resumo do hardware no cabeçalho.");

            CpuText = L("Common_NotAvailable");
            MemoryText = L("Common_NotAvailable");
            GpuText = L("Common_NotAvailable");
            OsText = L("Common_NotAvailable");
        }

        IsAdmin = _elevation.IsElevated;
        IsGameModeActive = _gameMode.IsActive;

        StartUptimeTicker();
    }

    /// <summary>Navega para uma tela pela chave.</summary>
    /// <param name="navigationKey">Chave (ex.: "dashboard").</param>
    [RelayCommand]
    private void Navigate(string navigationKey)
    {
        if (string.IsNullOrWhiteSpace(navigationKey))
        {
            return;
        }

        if (!_navigation.NavigateTo(navigationKey))
        {
            StatusMessage = L("Msg_NavigationFailed");
        }
    }

    /// <summary>Volta para a tela anterior.</summary>
    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    /// <summary>Reinicia o aplicativo com privilégios de administrador.</summary>
    [RelayCommand]
    private async Task ElevateAsync()
    {
        if (IsAdmin)
        {
            return;
        }

        StatusMessage = L("Dlg_ElevationMessage");

        var restarted = await _elevation.RestartElevatedAsync().ConfigureAwait(true);

        if (restarted)
        {
            // O processo elevado assumiu: encerra esta instância.
            System.Windows.Application.Current?.Shutdown();
        }
        else
        {
            StatusMessage = L("Msg_NeedAdmin");
        }
    }

    /// <summary>Ativa/desativa o Modo Gamer a partir do cabeçalho.</summary>
    [RelayCommand]
    private async Task ToggleGameModeAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (_gameMode.IsActive)
            {
                await _gameMode.DeactivateAsync().ConfigureAwait(true);
                StatusMessage = L("Game_Inactive");
            }
            else
            {
                var enabledTweaks = _gameMode.AvailableTweaks
                    .Where(t => t.IsEnabled)
                    .Select(t => t.Id)
                    .ToList();

                var state = await _gameMode.ActivateAsync(enabledTweaks).ConfigureAwait(true);

                StatusMessage = state.Error ?? L("Game_Active");
            }

            IsGameModeActive = _gameMode.IsActive;
        }, L("Common_Loading"));
    }

    /// <summary>Recarrega o inventário (botão "Atualizar" do cabeçalho).</summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadSummaryAsync();

    /// <inheritdoc />
    public override void OnNavigatedFrom()
    {
        _uptimeCts?.Cancel();
        _uptimeCts?.Dispose();
        _uptimeCts = null;
    }

    /// <summary>Aplica o perfil do sistema aos textos do cabeçalho.</summary>
    private void ApplyProfile(SystemProfile profile)
    {
        CpuText = profile.Cpu.ShortDescription;
        MemoryText = $"{profile.Memory.TotalFormatted} · {profile.Memory.SpeedMHz} MHz";
        GpuText = profile.PrimaryGpu?.Name ?? L("Common_NotAvailable");
        OsText = profile.OperatingSystem.ShortDescription;
        UptimeText = FormatUptime(profile.OperatingSystem.Uptime);
    }

    /// <summary>Atualiza o uptime a cada minuto sem pesar na UI.</summary>
    private void StartUptimeTicker()
    {
        _uptimeCts?.Cancel();
        _uptimeCts = new CancellationTokenSource();

        var token = _uptimeCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);

                    var profile = await _systemInformation.GetSystemProfileAsync(token).ConfigureAwait(false);

                    OnUiThread(() => UptimeText = FormatUptime(profile.OperatingSystem.Uptime));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Falha ao atualizar o uptime do cabeçalho.");
                }
            }
        }, token);
    }

    private static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays} d {uptime.Hours} h"
            : uptime.TotalHours >= 1
                ? $"{(int)uptime.TotalHours} h {uptime.Minutes} min"
                : $"{(int)uptime.TotalMinutes} min";

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        CurrentViewModel = _navigation.CurrentViewModel;

        OnPropertyChanged(nameof(CurrentKey));
        OnPropertyChanged(nameof(CanGoBack));
    }

    private void OnGameModeStateChanged(object? sender, GameModeState state) =>
        OnUiThread(() =>
        {
            IsGameModeActive = state.IsActive;

            if (!string.IsNullOrWhiteSpace(state.Error))
            {
                StatusMessage = state.Error;
            }
        });
}

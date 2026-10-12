using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>Linha de um jogo na biblioteca do Modo Jogo.</summary>
public sealed partial class GameLibraryRow : ObservableObject
{
    public GameLibraryRow(AppOptimizationProfile profile, bool isCustom)
    {
        Profile = profile;
        IsCustom = isCustom;
    }

    public AppOptimizationProfile Profile { get; }
    public bool IsCustom { get; }

    public string AppKey => Profile.AppKey;
    public string DisplayName => Profile.DisplayName;
    public string IconGlyph => Profile.IconGlyph;
    public string Notes => Profile.Notes;
    public string ExpectedGain => Profile.ExpectedGain;
    public string ProcessNamesText => string.Join(", ", Profile.ProcessNames);
    public string DetectionHint => Profile.DetectionHint;

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _runningPid;

    public string StateText => IsRunning
        ? $"Em execução (PID {RunningPid})"
        : IsInstalled ? "Instalado" : "Não detetado";

    public HealthLevel StateHealth => IsRunning ? HealthLevel.Otimo : IsInstalled ? HealthLevel.Atencao : HealthLevel.Desconhecido;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(StateText));

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(StateText));
}

/// <summary>
/// ViewModel do Modo Jogo (3.4-e).
///
/// Como funciona: o <see cref="GameModeService"/> tem um watchdog que verifica a cada 3 segundos se
/// um dos processos da biblioteca está em execução. Quando deteta um jogo, aplica automaticamente
/// prioridade alta, afinidade e plano de energia, limita prioridades dos processos de fundo e mantém
/// a memória disponível. Quando o jogo fecha, repõe tudo — sem intervenção do utilizador.
///
/// Esta página mostra o estado em tempo real (evento StatusChanged), permite ativar/desativar o
/// modo, escolher um jogo manualmente e adicionar jogos personalizados.
/// </summary>
public sealed partial class GameModeViewModel : ViewModelBase
{
    private readonly GameModeService _gameMode;
    private readonly ShellStateService _shell;
    private readonly IDialogService _dialogs;

    public GameModeViewModel(
        NotificationService notifications,
        GameModeService gameMode,
        ShellStateService shell,
        IDialogService dialogs)
        : base(notifications)
    {
        _gameMode = gameMode;
        _shell = shell;
        _dialogs = dialogs;

        Title = "Modo Jogo";
        Subtitle = "Deteção automática do jogo e otimização enquanto joga";
        IconKind = "ControllerClassic";

        _gameMode.StatusChanged += OnStatusChanged;

        LoadLibrary();
        UpdateStatus(_gameMode.Status);
    }

    // ---- Estado ---------------------------------------------------------------

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _stateText = "Inativo";
    [ObservableProperty] private HealthLevel _stateHealth = HealthLevel.Desconhecido;
    [ObservableProperty] private string _activeGameText = "Nenhum jogo ativo";
    [ObservableProperty] private string _throttledText = "0 processos de fundo limitados";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _lastMessage = string.Empty;
    [ObservableProperty] private string _memoryText = "—";
    [ObservableProperty] private bool _autoActivate = true;

    public ObservableCollection<GameLibraryRow> Library { get; } = new();

    /// <summary>O que o Modo Jogo faz (explicação honesta, sem promessas mágicas).</summary>
    public IReadOnlyList<string> Features => GameModeService.Features;

    /// <summary>Biblioteca filtrada pela pesquisa (mostrada na lista).</summary>
    public IEnumerable<GameLibraryRow> FilteredLibrary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return Library;

            var term = SearchText.Trim();
            return Library.Where(r =>
                r.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.ProcessNamesText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredLibrary));

    /// <summary>Estado geral apresentado no cabeçalho da aplicação.</summary>
    private void OnStatusChanged(object? sender, GameModeStatus status) => UpdateStatus(status);

    private void UpdateStatus(GameModeStatus status)
    {
        IsEnabled = status.State is GameModeState.Ativo or GameModeState.AguardaJogo or GameModeState.Ativado;
        StateText = status.StateText;
        StateHealth = status.State switch
        {
            GameModeState.Ativo => HealthLevel.Otimo,
            GameModeState.AguardaJogo => HealthLevel.Atencao,
            GameModeState.Ativado => HealthLevel.Otimo,
            _ => HealthLevel.Desconhecido,
        };

        ActiveGameText = status.State == GameModeState.Ativo && !string.IsNullOrWhiteSpace(status.ActiveGameName)
            ? $"{status.ActiveGameName} (PID {status.ActivePid})"
            : "Nenhum jogo ativo";

        ThrottledText = $"{status.BackgroundProcessesThrottled} processos de fundo limitados";
        LastMessage = status.LastMessage;
        MemoryText = $"{_gameMode.GetAvailableMemoryGb():N1} GB de RAM disponível";

        _shell.IsGameModeActive = status.State == GameModeState.Ativo;
    }

    // ---- Biblioteca ------------------------------------------------------------

    private void LoadLibrary()
    {
        var detectedGames = _gameMode.Library;

        Library.Clear();

        foreach (var profile in detectedGames)
            Library.Add(new GameLibraryRow(profile, isCustom: false));

        foreach (var row in Library)
        {
            var detected = _gameMode.Library.FirstOrDefault(g => g.AppKey == row.AppKey);
            row.IsInstalled = detected?.IsInstalled ?? false;
            row.IsRunning = detected?.IsRunning ?? false;
            row.RunningPid = detected?.RunningPid ?? 0;
        }

        OnPropertyChanged(nameof(FilteredLibrary));
    }

    // ---- Ações -----------------------------------------------------------------

    /// <summary>Liga/desliga o Modo Jogo (com watchdog automático).</summary>
    [RelayCommand]
    private void ToggleEnabled()
    {
        var enable = !IsEnabled;
        var result = _gameMode.SetEnabled(enable, AutoActivate);

        Report(result, "Modo Jogo");
        UpdateStatus(_gameMode.Status);
    }

    /// <summary>Ativa manualmente o perfil para um jogo da biblioteca.</summary>
    [RelayCommand]
    private void ActivateFor(GameLibraryRow? row)
    {
        if (row is null) return;

        Report(_gameMode.ActivateFor(row.Profile), "Modo Jogo");
        UpdateStatus(_gameMode.Status);
    }

    /// <summary>Repõe tudo imediatamente (prioridades, energia e processos de fundo).</summary>
    [RelayCommand]
    private void Deactivate()
    {
        Report(_gameMode.DeactivateNow(), "Modo Jogo");
        UpdateStatus(_gameMode.Status);
    }

    /// <summary>Adiciona um jogo personalizado a partir do executável escolhido pelo utilizador.</summary>
    [RelayCommand]
    private async Task AddCustomGameAsync()
    {
        var path = _dialogs.PickFile("Executáveis (*.exe)|*.exe", "Escolher o executável do jogo");
        if (string.IsNullOrWhiteSpace(path)) return;

        await RunBusyAsync(() =>
        {
            var result = _gameMode.AddCustomGame(path);
            Report(result, "Modo Jogo");

            if (result.Success)
            {
                LoadLibrary();
                OnPropertyChanged(nameof(FilteredLibrary));
            }

            return Task.CompletedTask;
        }, "A adicionar o jogo…");
    }

    /// <summary>Remove um jogo adicionado manualmente.</summary>
    [RelayCommand]
    private async Task RemoveCustomAsync(GameLibraryRow? row)
    {
        if (row is null || !row.IsCustom) return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Remover jogo",
            $"Remover «{row.DisplayName}» da biblioteca do Modo Jogo?",
            "Remover");

        if (!confirmed) return;

        Report(_gameMode.RemoveCustomGame(row.AppKey), "Modo Jogo");
        LoadLibrary();
        OnPropertyChanged(nameof(FilteredLibrary));
    }

    [RelayCommand]
    private void RefreshLibrary()
    {
        LoadLibrary();
        UpdateStatus(_gameMode.Status);
        SetStatus("Biblioteca atualizada com o estado real dos processos.");
    }

    public override async Task OnNavigatedToAsync(object? parameter = null)
    {
        await base.OnNavigatedToAsync(parameter);

        RefreshLibrary();
        AutoActivate = true;
    }

    public override Task OnNavigatedFromAsync()
    {
        // O serviço continua a funcionar em segundo plano (é o objetivo do Modo Jogo):
        // apenas deixamos de receber notificações quando a página não está visível.
        return base.OnNavigatedFromAsync();
    }
}

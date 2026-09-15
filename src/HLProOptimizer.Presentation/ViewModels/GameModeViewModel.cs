using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Presentation.ViewModels.Items;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// Tela "Modo Gamer": ativa/desativa o conjunto de tweaks de desempenho,
/// mostra o ganho estimado, permite priorizar um processo de jogo e reage à
/// detecção automática de jogos em execução.
/// </summary>
/// <remarks>
/// <para>
/// <b>Progresso textual.</b> <see cref="IGameModeService.ActivateAsync"/> reporta
/// <c>IProgress&lt;string&gt;</c> (um tweak por vez). Adaptamos para
/// <see cref="ScanProgress"/> — o contrato do <c>ProgressDialog</c> — sem duplicar a
/// infraestrutura de progresso.
/// </para>
/// <para>
/// <b>Detecção de jogo.</b> O <see cref="IGameSessionWatcher"/> só é ligado enquanto
/// a tela está visível (<see cref="OnNavigatedTo"/>/<see cref="OnNavigatedFrom"/>):
/// polling de processos não precisa rodar quando o usuário está em outra tela.
/// </para>
/// </remarks>
public sealed partial class GameModeViewModel : ViewModelBase
{
    private readonly IGameModeService _gameMode;
    private readonly IProcessService _processes;
    private readonly IGameSessionWatcher _watcher;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly IElevationService _elevation;

    private GameModeState _state = GameModeState.Inactive;
    private bool _watcherHooked;

    /// <summary>Cria o ViewModel do Modo Gamer.</summary>
    /// <param name="gameMode">Serviço do Modo Gamer.</param>
    /// <param name="processes">Processos (boost/prioridade).</param>
    /// <param name="watcher">Detecção de jogos em execução.</param>
    /// <param name="dialogs">Diálogos.</param>
    /// <param name="settings">Configurações.</param>
    /// <param name="elevation">Elevação sob demanda.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public GameModeViewModel(
        IGameModeService gameMode,
        IProcessService processes,
        IGameSessionWatcher watcher,
        IDialogService dialogs,
        ISettingsService settings,
        IElevationService elevation,
        ILocalizationService localization,
        ILogger<GameModeViewModel> logger)
        : base(localization, logger)
    {
        _gameMode = gameMode;
        _processes = processes;
        _watcher = watcher;
        _dialogs = dialogs;
        _settings = settings;
        _elevation = elevation;

        foreach (var tweak in _gameMode.AvailableTweaks.OrderBy(t => t.Order))
        {
            Tweaks.Add(new GameTweakViewModel(tweak));
        }

        AutoDisableOnGameExit = _settings.Current.GameModeAutoDisableOnGameExit;
        KillBackgroundProcesses = _settings.Current.GameModeKillBackgroundProcesses;
        IsElevated = _elevation.IsElevated;

        ApplyState(_gameMode.CurrentState);
        _gameMode.StateChanged += OnGameModeStateChanged;
    }

    /// <summary>Tweaks disponíveis.</summary>
    public ObservableCollection<GameTweakViewModel> Tweaks { get; } = [];

    /// <summary>Log em tempo real da última ativação.</summary>
    public ObservableCollection<string> Log { get; } = [];

    /// <summary>Indica se há linhas no log.</summary>
    public bool HasLog => Log.Count > 0;

    /// <summary>Indica se o Modo Gamer está ativo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsInactive))]
    private bool _isActive;

    /// <summary>Indica se o Modo Gamer está inativo.</summary>
    public bool IsInactive => !IsActive;

    /// <summary>Texto de estado.</summary>
    public string StatusText => IsActive ? L("Game_Active") : L("Game_Inactive");

    /// <summary>Momento da ativação.</summary>
    [ObservableProperty]
    private string _activeSinceText = string.Empty;

    /// <summary>Duração da sessão ativa.</summary>
    [ObservableProperty]
    private string _durationText = "—";

    /// <summary>Ganho estimado de FPS (soma dos tweaks habilitados).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedFpsGainText))]
    private int _estimatedFpsGain;

    /// <summary>Texto do ganho estimado.</summary>
    public string EstimatedFpsGainText => $"+{EstimatedFpsGain} FPS";

    /// <summary>Tweaks aplicados com sucesso.</summary>
    [ObservableProperty]
    private int _appliedCount;

    /// <summary>Tweaks que falharam.</summary>
    [ObservableProperty]
    private int _failedCount;

    /// <summary>Processos finalizados ao ativar.</summary>
    [ObservableProperty]
    private string _terminatedText = string.Empty;

    /// <summary>Memória liberada.</summary>
    [ObservableProperty]
    private string _freedMemoryText = "0 MB";

    /// <summary>Jogo atualmente detectado (ou texto informativo).</summary>
    [ObservableProperty]
    private string _currentGameText = string.Empty;

    /// <summary>Desativar automaticamente quando o jogo fechar.</summary>
    [ObservableProperty]
    private bool _autoDisableOnGameExit;

    /// <summary>Encerrar processos em segundo plano ao ativar.</summary>
    [ObservableProperty]
    private bool _killBackgroundProcesses;

    /// <summary>Nome do processo a priorizar (entrada do usuário).</summary>
    [ObservableProperty]
    private string _boostProcessName = string.Empty;

    /// <summary>Indica se o processo é administrador.</summary>
    [ObservableProperty]
    private bool _isElevated;

    /// <inheritdoc />
    public override void OnNavigatedTo(object? parameter)
    {
        if (!_watcherHooked)
        {
            _watcher.GameStarted += OnGameStarted;
            _watcher.GameStopped += OnGameStopped;
            _watcherHooked = true;
        }

        _watcher.Start();

        CurrentGameText = _watcher.CurrentGame ?? L("Game_NoGameDetected");
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom()
    {
        _watcher.Stop();

        if (_watcherHooked)
        {
            _watcher.GameStarted -= OnGameStarted;
            _watcher.GameStopped -= OnGameStopped;
            _watcherHooked = false;
        }
    }

    /// <summary>Ativa o Modo Gamer com os tweaks habilitados.</summary>
    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await PersistPreferencesAsync().ConfigureAwait(true);

        var enabledIds = Tweaks.Where(t => t.IsEnabled).Select(t => t.Id).ToList();

        if (enabledIds.Count == 0)
        {
            await _dialogs.ShowWarningAsync(L("Game_Title"), L("Game_NoTweaksApplied")).ConfigureAwait(true);
            return;
        }

        Log.Clear();
        OnPropertyChanged(nameof(HasLog));

        var completed = await _dialogs.ShowProgressAsync(
            L("Game_Activate"),
            async (progress, cancellationToken) =>
            {
                var textProgress = new Progress<string>(message =>
                {
                    AppendLog(message);
                    progress.Report(new ScanProgress(IssueCategory.Performance, L("Game_Title"), 0, enabledIds.Count, message));
                });

                _state = await _gameMode
                    .ActivateAsync(enabledIds, textProgress, cancellationToken)
                    .ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!completed)
        {
            StatusMessage = L("Dlg_CancelledTitle");
            return;
        }

        ApplyState(_state);

        if (!string.IsNullOrWhiteSpace(_state.Error))
        {
            await _dialogs.ShowWarningAsync(L("Game_Title"), _state.Error).ConfigureAwait(true);
        }
        else
        {
            StatusMessage = LF("Game_ActivatedSummary", AppliedCount, FreedMemoryText);
        }
    }

    /// <summary>Desativa o Modo Gamer revertendo os tweaks.</summary>
    [RelayCommand]
    private async Task DeactivateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            _state = await _gameMode.DeactivateAsync().ConfigureAwait(true);

            ApplyState(_state);

            StatusMessage = L("Game_Inactive");
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Prioriza o processo informado (alta prioridade + boost de memória).</summary>
    [RelayCommand]
    private async Task BoostProcessAsync()
    {
        if (string.IsNullOrWhiteSpace(BoostProcessName))
        {
            await _dialogs.ShowWarningAsync(L("Game_BoostProcess"), L("Game_EnterProcessName")).ConfigureAwait(true);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var boosted = await _processes.BoostAsync(BoostProcessName.Trim()).ConfigureAwait(true);

            StatusMessage = boosted
                ? LF("Game_BoostedProcess", BoostProcessName.Trim())
                : LF("Game_ProcessNotFound", BoostProcessName.Trim());
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Habilita todos os tweaks.</summary>
    [RelayCommand]
    private void EnableAllTweaks() => SetAllTweaks(true);

    /// <summary>Desabilita todos os tweaks.</summary>
    [RelayCommand]
    private void DisableAllTweaks() => SetAllTweaks(false);

    /// <summary>Habilita apenas os tweaks recomendados (sem exigência de administrador).</summary>
    [RelayCommand]
    private void EnableRecommendedTweaks()
    {
        foreach (var tweak in Tweaks)
        {
            tweak.IsEnabled = !tweak.RequiresAdmin || IsElevated;
        }

        RecalculateGain();
    }

    /// <summary>Limpa o log.</summary>
    [RelayCommand]
    private void ClearLog()
    {
        Log.Clear();
        OnPropertyChanged(nameof(HasLog));
    }

    /// <summary>Recarrega o estado vindo do serviço.</summary>
    [RelayCommand]
    private void Refresh()
    {
        IsElevated = _elevation.IsElevated;
        ApplyState(_gameMode.CurrentState);
        RecalculateGain();
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    private void SetAllTweaks(bool enabled)
    {
        foreach (var tweak in Tweaks)
        {
            tweak.IsEnabled = enabled;
        }

        RecalculateGain();
    }

    private void RecalculateGain() =>
        EstimatedFpsGain = Tweaks.Where(t => t.IsEnabled).Sum(t => t.EstimatedFpsGain);

    private void ApplyState(GameModeState state)
    {
        _state = state;

        IsActive = state.IsActive;
        AppliedCount = state.AppliedTweaks.Count;
        FailedCount = state.FailedTweaks.Count;
        FreedMemoryText = state.FreedMemoryBytes > 0 ? ByteFormat.Format(state.FreedMemoryBytes) : "0 MB";
        TerminatedText = state.TerminatedProcesses.Count == 0
            ? L("Common_None")
            : string.Join(", ", state.TerminatedProcesses.Take(8));

        ActiveSinceText = state.ActivatedAt is { } at ? at.ToString("HH:mm:ss") : string.Empty;
        DurationText = state.IsActive && state.ActiveDuration > TimeSpan.Zero
            ? state.ActiveDuration.ToString(@"hh\:mm\:ss")
            : "—";

        foreach (var tweak in Tweaks)
        {
            tweak.SyncFrom(state);
        }

        RecalculateGain();
    }

    /// <summary>Persiste as preferências da tela antes de ativar.</summary>
    private async Task PersistPreferencesAsync()
    {
        var settings = _settings.Current.Clone();

        settings.GameModeAutoDisableOnGameExit = AutoDisableOnGameExit;
        settings.GameModeKillBackgroundProcesses = KillBackgroundProcesses;

        await _settings.SaveAsync(settings).ConfigureAwait(true);
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        OnUiThread(() =>
        {
            Log.Add($"{DateTime.Now:HH:mm:ss}  {message}");

            while (Log.Count > 300)
            {
                Log.RemoveAt(0);
            }

            OnPropertyChanged(nameof(HasLog));
        });
    }

    private void OnGameModeStateChanged(object? sender, GameModeState state) => OnUiThread(() => ApplyState(state));

    private void OnGameStarted(object? sender, string gameName)
    {
        OnUiThread(() =>
        {
            CurrentGameText = gameName;
            AppendLog(LF("Game_Detected", gameName));
        });

        if (_gameMode.IsActive || !_settings.Current.GameModeAutoStart)
        {
            return;
        }

        // Fire-and-forget controlado: a auto-ativação não pode travar o evento do watcher.
        _ = AutoActivateAsync(gameName);
    }

    private void OnGameStopped(object? sender, string gameName)
    {
        OnUiThread(() =>
        {
            CurrentGameText = L("Game_NoGameDetected");
            AppendLog(LF("Game_Exited", gameName));
        });

        if (!_gameMode.IsActive || !AutoDisableOnGameExit)
        {
            return;
        }

        _ = AutoDeactivateAsync();
    }

    /// <summary>Ativa o Modo Gamer automaticamente quando um jogo é detectado.</summary>
    private async Task AutoActivateAsync(string gameName)
    {
        try
        {
            var state = await _gameMode.ActivateAsync().ConfigureAwait(false);

            OnUiThread(() =>
            {
                ApplyState(state);
                StatusMessage = LF("Game_AutoActivated", gameName);
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha na ativação automática do Modo Gamer para {Game}.", gameName);
        }
    }

    /// <summary>Desativa o Modo Gamer quando o jogo é encerrado.</summary>
    private async Task AutoDeactivateAsync()
    {
        try
        {
            var state = await _gameMode.DeactivateAsync().ConfigureAwait(false);

            OnUiThread(() =>
            {
                ApplyState(state);
                StatusMessage = L("Game_AutoDeactivated");
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha na desativação automática do Modo Gamer.");
        }
    }
}

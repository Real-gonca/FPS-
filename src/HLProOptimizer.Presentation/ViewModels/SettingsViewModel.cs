using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Core.Plugins;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// Tela "Configurações": geral, otimização, Modo Gamer, monitoramento, atualizações,
/// plugins e sobre.
/// </summary>
/// <remarks>
/// <para>
/// <b>Edição transacional.</b> Trabalhamos sobre um <see cref="AppSettings.Clone"/>:
/// nada é persistido até o usuário clicar em "Salvar" (<see cref="HasChanges"/> habilita
/// o botão). "Restaurar padrões" volta ao estado de fábrica via
/// <see cref="ISettingsService.ResetToDefaultsAsync"/>.
/// </para>
/// <para>
/// <b>Iniciar com o Windows</b> é aplicado na chave <c>HKCU\...\Run</c> (não exige
/// administrador) usando o caminho real do executável (<see cref="ISystemPaths"/>).
/// </para>
/// <para>
/// <b>Idioma/tema</b> são aplicados imediatamente; como os textos dos ViewModels são
/// resolvidos na construção, avisamos que a troca completa pede reinício.
/// </para>
/// </remarks>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "HLProOptimizer";

    private readonly ISettingsService _settings;
    private readonly IElevationService _elevation;
    private readonly IUpdateService _updates;
    private readonly ISystemPaths _paths;
    private readonly IPluginManager _plugins;
    private readonly IRegistryService _registry;
    private readonly IDialogService _dialogs;
    private readonly IActionHistoryService _history;

    private AppSettings _draft;

    /// <summary>Cria o ViewModel de configurações.</summary>
    /// <param name="settings">Serviço de configurações.</param>
    /// <param name="elevation">Elevação.</param>
    /// <param name="updates">Atualizações do produto.</param>
    /// <param name="paths">Caminhos do aplicativo.</param>
    /// <param name="plugins">Gerenciador de plugins.</param>
    /// <param name="registry">Registro (iniciar com o Windows).</param>
    /// <param name="dialogs">Diálogos.</param>
    /// <param name="history">Histórico de ações.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public SettingsViewModel(
        ISettingsService settings,
        IElevationService elevation,
        IUpdateService updates,
        ISystemPaths paths,
        IPluginManager plugins,
        IRegistryService registry,
        IDialogService dialogs,
        IActionHistoryService history,
        ILocalizationService localization,
        ILogger<SettingsViewModel> logger)
        : base(localization, logger)
    {
        _settings = settings;
        _elevation = elevation;
        _updates = updates;
        _paths = paths;
        _plugins = plugins;
        _registry = registry;
        _dialogs = dialogs;
        _history = history;

        _draft = _settings.Current.Clone();

        Languages =
        [
            new LanguageOption(AppLanguage.PtBr, "Português (Brasil)"),
            new LanguageOption(AppLanguage.En, "English"),
            new LanguageOption(AppLanguage.Es, "Español")
        ];

        Themes =
        [
            new ThemeOption(ThemeMode.Dark, L("Settings_Theme_Dark")),
            new ThemeOption(ThemeMode.Light, L("Settings_Theme_Light")),
            new ThemeOption(ThemeMode.Auto, L("Settings_Theme_Auto"))
        ];

        Levels = Enum.GetValues<OptimizationLevel>();
        Modes = Enum.GetValues<OptimizationMode>();
        Frequencies = Enum.GetValues<UpdateFrequency>();
        Channels = Enum.GetValues<UpdateChannel>();
        IntervalOptions = [1, 2, 5];
        HistoryOptions = [30, 60, 120, 300];

        VersionText = _updates.CurrentVersion;
        DataFolderText = _paths.ApplicationDataDirectory;
        LogFolderText = _paths.LogDirectory;
        BackupFolderText = _paths.BackupDirectory;
        PluginFolderText = _paths.PluginDirectory;
        IsElevated = _elevation.IsElevated;
    }

    /// <summary>Idiomas suportados.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; }

    /// <summary>Temas suportados.</summary>
    public IReadOnlyList<ThemeOption> Themes { get; }

    /// <summary>Níveis de otimização.</summary>
    public IReadOnlyList<OptimizationLevel> Levels { get; }

    /// <summary>Modos de otimização.</summary>
    public IReadOnlyList<OptimizationMode> Modes { get; }

    /// <summary>Frequências de verificação de atualização.</summary>
    public IReadOnlyList<UpdateFrequency> Frequencies { get; }

    /// <summary>Canais de atualização.</summary>
    public IReadOnlyList<UpdateChannel> Channels { get; }

    /// <summary>Intervalos de monitoramento (segundos).</summary>
    public IReadOnlyList<int> IntervalOptions { get; }

    /// <summary>Janelas de histórico (segundos).</summary>
    public IReadOnlyList<int> HistoryOptions { get; }

    /// <summary>Plugins carregados.</summary>
    public ObservableCollection<PluginDescriptor> Plugins { get; } = [];

    /// <summary>Indica se há plugins.</summary>
    public bool HasPlugins => Plugins.Count > 0;

    /// <summary>Versão do produto.</summary>
    public string VersionText { get; }

    /// <summary>Pasta de dados.</summary>
    public string DataFolderText { get; }

    /// <summary>Pasta de logs.</summary>
    public string LogFolderText { get; }

    /// <summary>Pasta de backups.</summary>
    public string BackupFolderText { get; }

    /// <summary>Pasta de plugins.</summary>
    public string PluginFolderText { get; }

    /// <summary>Indica se há alterações não salvas.</summary>
    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>Indica se o processo é administrador.</summary>
    [ObservableProperty]
    private bool _isElevated;

    /// <summary>Texto de estado da atualização.</summary>
    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    /// <summary>Indica se há atualização disponível.</summary>
    [ObservableProperty]
    private bool _isUpdateAvailable;

    /// <summary>Notas da versão disponível.</summary>
    [ObservableProperty]
    private string _releaseNotesText = string.Empty;

    // ------------------------- Geral ------------------------------------

    /// <summary>Idioma selecionado.</summary>
    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    /// <summary>Tema selecionado.</summary>
    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    /// <summary>Iniciar com o Windows.</summary>
    [ObservableProperty]
    private bool _startWithWindows;

    /// <summary>Minimizar para a bandeja ao fechar.</summary>
    [ObservableProperty]
    private bool _minimizeToTray = true;

    /// <summary>Exibir notificações.</summary>
    [ObservableProperty]
    private bool _notificationsEnabled = true;

    /// <summary>Iniciar minimizado.</summary>
    [ObservableProperty]
    private bool _startMinimized;

    /// <summary>Exibir ícone na bandeja.</summary>
    [ObservableProperty]
    private bool _showTrayIcon = true;

    // ------------------------- Otimização ---------------------------------

    /// <summary>Nível de agressividade.</summary>
    [ObservableProperty]
    private OptimizationLevel _optimizationLevel = OptimizationLevel.Balanced;

    /// <summary>Modo padrão de otimização.</summary>
    [ObservableProperty]
    private OptimizationMode _defaultOptimizationMode = OptimizationMode.Full;

    /// <summary>Backup automático antes de alterações.</summary>
    [ObservableProperty]
    private bool _autoBackupEnabled = true;

    /// <summary>Ponto de restauração antes de otimizar.</summary>
    [ObservableProperty]
    private bool _createRestorePointBeforeChanges = true;

    /// <summary>Confirmar antes de apagar arquivos.</summary>
    [ObservableProperty]
    private bool _confirmBeforeDelete = true;

    // ------------------------- Modo Gamer ---------------------------------

    /// <summary>Reativar o Modo Gamer ao iniciar.</summary>
    [ObservableProperty]
    private bool _gameModeAutoStart;

    /// <summary>Desativar quando o jogo fechar.</summary>
    [ObservableProperty]
    private bool _gameModeAutoDisableOnGameExit;

    /// <summary>Encerrar processos em segundo plano.</summary>
    [ObservableProperty]
    private bool _gameModeKillBackgroundProcesses = true;

    /// <summary>Overlay em jogos (recurso futuro).</summary>
    [ObservableProperty]
    private bool _gameOverlayEnabled;

    // ------------------------- Monitoramento ------------------------------

    /// <summary>Intervalo de coleta (segundos).</summary>
    [ObservableProperty]
    private int _monitoringIntervalSeconds = 1;

    /// <summary>Janela de histórico (segundos).</summary>
    [ObservableProperty]
    private int _monitoringHistorySeconds = 60;

    /// <summary>Medir latência de rede.</summary>
    [ObservableProperty]
    private bool _measureNetworkLatency;

    /// <summary>Host do teste de latência.</summary>
    [ObservableProperty]
    private string _latencyProbeHost = "8.8.8.8";

    // ------------------------- Atualizações -------------------------------

    /// <summary>Verificar atualizações automaticamente.</summary>
    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    /// <summary>Frequência da verificação.</summary>
    [ObservableProperty]
    private UpdateFrequency _updateFrequency = UpdateFrequency.Weekly;

    /// <summary>Instalar automaticamente.</summary>
    [ObservableProperty]
    private bool _autoInstallUpdates;

    /// <summary>Canal de atualização.</summary>
    [ObservableProperty]
    private UpdateChannel _updateChannel = UpdateChannel.Stable;

    /// <inheritdoc />
    public override void OnNavigatedTo(object? parameter)
    {
        _ = LoadAsync();
    }

    /// <summary>Recarrega o rascunho a partir das configurações persistidas.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var current = await _settings.LoadAsync().ConfigureAwait(true);

            _draft = current.Clone();

            ApplyToUi(_draft);

            IsElevated = _elevation.IsElevated;

            await LoadPluginsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Salva as alterações.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        await RunBusyAsync(async () =>
        {
            var updated = BuildSettings();

            var languageChanged = updated.Language != _settings.Current.Language;

            await _settings.SaveAsync(updated).ConfigureAwait(true);

            ApplyStartupRegistration(updated.StartWithWindows);

            _draft = updated.Clone();

            await _history.RecordAsync(ActionKind.Settings, L("Common_Save"), L("Settings_Saved"), success: true)
                .ConfigureAwait(true);

            HasChanges = false;
            StatusMessage = L("Settings_Saved");

            if (languageChanged)
            {
                await _dialogs.ShowInfoAsync(L("Settings_Language"), L("Msg_RestartRequired")).ConfigureAwait(true);
            }
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Restaura as configurações de fábrica.</summary>
    [RelayCommand]
    private async Task ResetDefaultsAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            L("Settings_ResetDefaults"),
            L("Settings_ResetConfirm"),
            confirmText: L("Settings_ResetDefaults"),
            isDestructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var defaults = await _settings.ResetToDefaultsAsync().ConfigureAwait(true);

            _draft = defaults.Clone();

            ApplyToUi(_draft);

            ApplyStartupRegistration(defaults.StartWithWindows);

            HasChanges = false;
            StatusMessage = L("Settings_Saved");
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Verifica se há atualização do produto.</summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        await RunBusyAsync(async () =>
        {
            UpdateStatusText = L("Settings_CheckingUpdate");

            var result = await _updates.CheckAsync().ConfigureAwait(true);

            IsUpdateAvailable = result.IsAvailable;

            if (result.HasError)
            {
                UpdateStatusText = LF("Settings_UpdateError", result.Error);
                return;
            }

            ReleaseNotesText = result.ReleaseNotes;

            UpdateStatusText = result.IsAvailable
                ? LF("Settings_UpdateAvailable", result.LatestVersion)
                : LF("Settings_UpToDate", result.CurrentVersion);
        }, L("Settings_CheckingUpdate")).ConfigureAwait(true);
    }

    /// <summary>Baixa e instala a atualização disponível.</summary>
    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _updates.CheckAsync().ConfigureAwait(true);

            if (!result.IsAvailable)
            {
                UpdateStatusText = LF("Settings_UpToDate", result.CurrentVersion);
                return;
            }

            var installed = await _updates.InstallAsync(result).ConfigureAwait(true);

            UpdateStatusText = installed ? L("Msg_RestartRequired") : L("Settings_UpdateInstallFailed");
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Carrega a lista de plugins.</summary>
    [RelayCommand]
    private async Task LoadPluginsAsync()
    {
        try
        {
            await _plugins.LoadAsync().ConfigureAwait(true);

            OnUiThread(() =>
            {
                Plugins.Clear();

                foreach (var descriptor in _plugins.Descriptors)
                {
                    Plugins.Add(descriptor);
                }

                OnPropertyChanged(nameof(HasPlugins));
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao carregar os plugins.");
        }
    }

    /// <summary>Instala um plugin a partir de uma DLL escolhida pelo usuário.</summary>
    [RelayCommand]
    private async Task InstallPluginAsync()
    {
        var path = await _dialogs.ShowOpenFileDialogAsync(new FileDialogOptions(
            L("Settings_PluginsInstall"),
            "Biblioteca (*.dll)|*.dll")).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var descriptor = await _plugins.InstallAsync(path).ConfigureAwait(true);

            StatusMessage = descriptor is null ? L("Msg_GenericError") : LF("Settings_PluginInstalled", descriptor.Name);

            await LoadPluginsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Ativa/desativa um plugin.</summary>
    /// <param name="descriptor">Plugin.</param>
    [RelayCommand]
    private async Task TogglePluginAsync(PluginDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var applied = descriptor.IsEnabled
                ? await _plugins.DisableAsync(descriptor.Id).ConfigureAwait(true)
                : await _plugins.EnableAsync(descriptor.Id).ConfigureAwait(true);

            StatusMessage = applied ? L("Settings_Saved") : L("Msg_GenericError");

            await LoadPluginsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Desinstala um plugin.</summary>
    /// <param name="descriptor">Plugin.</param>
    [RelayCommand]
    private async Task UninstallPluginAsync(PluginDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            L("Settings_PluginsManage"),
            LF("Settings_PluginUninstallConfirm", descriptor.Name),
            confirmText: L("Common_Remove"),
            isDestructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var removed = await _plugins.UninstallAsync(descriptor.Id).ConfigureAwait(true);

            StatusMessage = removed ? LF("Settings_PluginRemoved", descriptor.Name) : L("Msg_GenericError");

            await LoadPluginsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Abre a pasta de logs.</summary>
    [RelayCommand]
    private Task OpenLogsFolderAsync() => OpenFolderAsync(_paths.LogDirectory);

    /// <summary>Abre a pasta de dados.</summary>
    [RelayCommand]
    private Task OpenDataFolderAsync() => OpenFolderAsync(_paths.ApplicationDataDirectory);

    /// <summary>Abre a pasta de backups.</summary>
    [RelayCommand]
    private Task OpenBackupFolderAsync() => OpenFolderAsync(_paths.BackupDirectory);

    /// <summary>Abre a pasta de plugins.</summary>
    [RelayCommand]
    private Task OpenPluginFolderAsync() => OpenFolderAsync(_paths.PluginDirectory);

    /// <summary>Apaga o histórico de ações.</summary>
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            L("Settings_ClearHistory"),
            L("Settings_ClearHistoryConfirm"),
            isDestructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _history.ClearAsync().ConfigureAwait(true);

            StatusMessage = L("Settings_HistoryCleared");
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Abre o canal de feedback (repositório do produto).</summary>
    [RelayCommand]
    private Task SendFeedbackAsync() => OpenFolderAsync("https://github.com/Real-gonca/FPS-/issues");

    /// <summary>Descarta as alterações não salvas.</summary>
    [RelayCommand]
    private void DiscardChanges()
    {
        _draft = _settings.Current.Clone();

        ApplyToUi(_draft);

        HasChanges = false;
        StatusMessage = string.Empty;
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    private Task OpenFolderAsync(string path)
    {
        try
        {
            return Task.Run(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            }));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao abrir {Path}.", path);

            return Task.CompletedTask;
        }
    }

    /// <summary>Grava/remove a entrada de inicialização automática no registro.</summary>
    private void ApplyStartupRegistration(bool enabled)
    {
        try
        {
            if (enabled)
            {
                _registry.SetString(RegistryHiveKind.CurrentUser, RunKeyPath, RunValueName, $"\"{_paths.ExecutablePath}\"");
            }
            else
            {
                _registry.DeleteValue(RegistryHiveKind.CurrentUser, RunKeyPath, RunValueName);
            }
        }
        catch (Exception ex)
        {
            // Não é fatal: o aplicativo funciona sem a entrada de inicialização.
            Logger.LogWarning(ex, "Falha ao registrar a inicialização automática.");

            StatusMessage = L("Settings_StartupRegistrationFailed");
        }
    }

    /// <summary>Copia o rascunho para as propriedades observáveis da UI.</summary>
    private void ApplyToUi(AppSettings settings)
    {
        SelectedLanguage = Languages.FirstOrDefault(l => l.Value == settings.Language) ?? Languages[0];
        SelectedTheme = Themes.FirstOrDefault(t => t.Value == settings.Theme) ?? Themes[0];

        _startWithWindows = ReadStartupRegistration();
        _minimizeToTray = settings.MinimizeToTray;
        _notificationsEnabled = settings.NotificationsEnabled;
        _startMinimized = settings.StartMinimized;
        _showTrayIcon = settings.ShowTrayIcon;

        _optimizationLevel = settings.OptimizationLevel;
        _defaultOptimizationMode = settings.DefaultOptimizationMode;
        _autoBackupEnabled = settings.AutoBackupEnabled;
        _createRestorePointBeforeChanges = settings.CreateRestorePointBeforeChanges;
        _confirmBeforeDelete = settings.ConfirmBeforeDelete;

        _gameModeAutoStart = settings.GameModeAutoStart;
        _gameModeAutoDisableOnGameExit = settings.GameModeAutoDisableOnGameExit;
        _gameModeKillBackgroundProcesses = settings.GameModeKillBackgroundProcesses;
        _gameOverlayEnabled = settings.GameOverlayEnabled;

        _monitoringIntervalSeconds = settings.MonitoringIntervalSeconds;
        _monitoringHistorySeconds = settings.MonitoringHistorySeconds;
        _measureNetworkLatency = settings.MeasureNetworkLatency;
        _latencyProbeHost = settings.LatencyProbeHost;

        _autoCheckUpdates = settings.AutoCheckUpdates;
        _updateFrequency = settings.UpdateFrequency;
        _autoInstallUpdates = settings.AutoInstallUpdates;
        _updateChannel = settings.UpdateChannel;

        NotifyUiProperties();

        HasChanges = false;
    }

    /// <summary>Lê o estado atual da entrada de inicialização no registro.</summary>
    private bool ReadStartupRegistration()
    {
        try
        {
            return _registry.ValueExists(RegistryHiveKind.CurrentUser, RunKeyPath, RunValueName);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Não foi possível ler a chave Run.");

            return false;
        }
    }

    /// <summary>Monta o <see cref="AppSettings"/> a partir da UI.</summary>
    private AppSettings BuildSettings()
    {
        var updated = _draft.Clone();

        updated.Language = SelectedLanguage?.Value ?? updated.Language;
        updated.Theme = SelectedTheme?.Value ?? updated.Theme;
        updated.StartWithWindows = StartWithWindows;
        updated.MinimizeToTray = MinimizeToTray;
        updated.NotificationsEnabled = NotificationsEnabled;
        updated.StartMinimized = StartMinimized;
        updated.ShowTrayIcon = ShowTrayIcon;
        updated.OptimizationLevel = OptimizationLevel;
        updated.DefaultOptimizationMode = DefaultOptimizationMode;
        updated.AutoBackupEnabled = AutoBackupEnabled;
        updated.CreateRestorePointBeforeChanges = CreateRestorePointBeforeChanges;
        updated.ConfirmBeforeDelete = ConfirmBeforeDelete;
        updated.GameModeAutoStart = GameModeAutoStart;
        updated.GameModeAutoDisableOnGameExit = GameModeAutoDisableOnGameExit;
        updated.GameModeKillBackgroundProcesses = GameModeKillBackgroundProcesses;
        updated.GameOverlayEnabled = GameOverlayEnabled;
        updated.MonitoringIntervalSeconds = MonitoringIntervalSeconds;
        updated.MonitoringHistorySeconds = MonitoringHistorySeconds;
        updated.MeasureNetworkLatency = MeasureNetworkLatency;
        updated.LatencyProbeHost = string.IsNullOrWhiteSpace(LatencyProbeHost) ? "8.8.8.8" : LatencyProbeHost.Trim();
        updated.AutoCheckUpdates = AutoCheckUpdates;
        updated.UpdateFrequency = UpdateFrequency;
        updated.AutoInstallUpdates = AutoInstallUpdates;
        updated.UpdateChannel = UpdateChannel;

        return updated;
    }

    /// <summary>Notifica a UI sobre todas as propriedades (usado após carregar o rascunho).</summary>
    private void NotifyUiProperties()
    {
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(MinimizeToTray));
        OnPropertyChanged(nameof(NotificationsEnabled));
        OnPropertyChanged(nameof(StartMinimized));
        OnPropertyChanged(nameof(ShowTrayIcon));
        OnPropertyChanged(nameof(OptimizationLevel));
        OnPropertyChanged(nameof(DefaultOptimizationMode));
        OnPropertyChanged(nameof(AutoBackupEnabled));
        OnPropertyChanged(nameof(CreateRestorePointBeforeChanges));
        OnPropertyChanged(nameof(ConfirmBeforeDelete));
        OnPropertyChanged(nameof(GameModeAutoStart));
        OnPropertyChanged(nameof(GameModeAutoDisableOnGameExit));
        OnPropertyChanged(nameof(GameModeKillBackgroundProcesses));
        OnPropertyChanged(nameof(GameOverlayEnabled));
        OnPropertyChanged(nameof(MonitoringIntervalSeconds));
        OnPropertyChanged(nameof(MonitoringHistorySeconds));
        OnPropertyChanged(nameof(MeasureNetworkLatency));
        OnPropertyChanged(nameof(LatencyProbeHost));
        OnPropertyChanged(nameof(AutoCheckUpdates));
        OnPropertyChanged(nameof(UpdateFrequency));
        OnPropertyChanged(nameof(AutoInstallUpdates));
        OnPropertyChanged(nameof(UpdateChannel));
    }

    // Qualquer mudança na UI marca o rascunho como "sujo".
    partial void OnStartWithWindowsChanged(bool value) => MarkDirty();
    partial void OnMinimizeToTrayChanged(bool value) => MarkDirty();
    partial void OnNotificationsEnabledChanged(bool value) => MarkDirty();
    partial void OnStartMinimizedChanged(bool value) => MarkDirty();
    partial void OnShowTrayIconChanged(bool value) => MarkDirty();
    partial void OnOptimizationLevelChanged(OptimizationLevel value) => MarkDirty();
    partial void OnDefaultOptimizationModeChanged(OptimizationMode value) => MarkDirty();
    partial void OnAutoBackupEnabledChanged(bool value) => MarkDirty();
    partial void OnCreateRestorePointBeforeChangesChanged(bool value) => MarkDirty();
    partial void OnConfirmBeforeDeleteChanged(bool value) => MarkDirty();
    partial void OnGameModeAutoStartChanged(bool value) => MarkDirty();
    partial void OnGameModeAutoDisableOnGameExitChanged(bool value) => MarkDirty();
    partial void OnGameModeKillBackgroundProcessesChanged(bool value) => MarkDirty();
    partial void OnGameOverlayEnabledChanged(bool value) => MarkDirty();
    partial void OnMonitoringIntervalSecondsChanged(int value) => MarkDirty();
    partial void OnMonitoringHistorySecondsChanged(int value) => MarkDirty();
    partial void OnMeasureNetworkLatencyChanged(bool value) => MarkDirty();
    partial void OnLatencyProbeHostChanged(string value) => MarkDirty();
    partial void OnAutoCheckUpdatesChanged(bool value) => MarkDirty();
    partial void OnUpdateFrequencyChanged(UpdateFrequency value) => MarkDirty();
    partial void OnAutoInstallUpdatesChanged(bool value) => MarkDirty();
    partial void OnUpdateChannelChanged(UpdateChannel value) => MarkDirty();

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        MarkDirty();

        if (value is not null)
        {
            // Aplica imediatamente os textos (novas telas já nascem no idioma certo).
            _ = _settings.SetLanguageAsync(value.Value);
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        MarkDirty();

        if (value is not null)
        {
            _ = _settings.SetThemeAsync(value.Value);
        }
    }

    private void MarkDirty() => HasChanges = true;
}

/// <summary>Opção de idioma.</summary>
/// <param name="Value">Idioma.</param>
/// <param name="Label">Nome nativo do idioma.</param>
public sealed record LanguageOption(AppLanguage Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>Opção de tema.</summary>
/// <param name="Value">Tema.</param>
/// <param name="Label">Rótulo localizado.</param>
public sealed record ThemeOption(ThemeMode Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

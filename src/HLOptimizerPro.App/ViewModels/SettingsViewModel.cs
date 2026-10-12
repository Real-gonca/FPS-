using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>
/// ViewModel da Configuração (3.8).
///
/// Reúne: idioma, resolução/escala da interface, iniciar com o Windows, notificações, modo escuro,
/// definições de segurança (ponto de restauro, backup do registo, confirmações), telemetria (com
/// consentimento explícito), perfil, histórico de atividades, deteção de sistema e ações de conta.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;
    private readonly ThemeService _theme;
    private readonly SystemInfoService _systemInfo;
    private readonly ActivityLogService _activity;
    private readonly AccountService _accounts;
    private readonly LicenseService _license;
    private readonly QuickActionsService _quickActions;
    private readonly MonitoringHistoryService _history;
    private readonly IDialogService _dialogs;
    private readonly NotificationService _notifications;

    public SettingsViewModel(
        NotificationService notifications,
        SettingsService settings,
        LocalizationService localization,
        ThemeService theme,
        SystemInfoService systemInfo,
        ActivityLogService activity,
        AccountService accounts,
        LicenseService license,
        QuickActionsService quickActions,
        MonitoringHistoryService history,
        IDialogService dialogs)
        : base(notifications)
    {
        _notifications = notifications;
        _settings = settings;
        _localization = localization;
        _theme = theme;
        _systemInfo = systemInfo;
        _activity = activity;
        _accounts = accounts;
        _license = license;
        _quickActions = quickActions;
        _history = history;
        _dialogs = dialogs;

        Title = "Configuração";
        Subtitle = "Preferências da aplicação, do sistema e da sua conta";
        IconKind = "Cog";

        LoadFromSettings();

        Languages = new ObservableCollection<LanguageOption>(LocalizationService.Options
            .Select(o => new LanguageOption(o.Code, o.Label)));

        Scales = new ObservableCollection<ScaleOption>(ThemeService.ScaleOptions
            .Select(s => new ScaleOption(s.Value, s.Label)));

        Intervals = new ObservableCollection<int>(new[] { 1, 2, 3, 5 });

        Resolutions = new ObservableCollection<string>(new[]
        {
            "Auto (resolução do sistema)",
        });

        SelectedResolution = Resolutions[0];
    }

    // ---- Opções -----------------------------------------------------------------

    public sealed record LanguageOption(string Code, string Label)
    {
        public override string ToString() => Label;
    }

    public sealed record ScaleOption(double Value, string Label)
    {
        public override string ToString() => Label;
    }

    public ObservableCollection<LanguageOption> Languages { get; }
    public ObservableCollection<ScaleOption> Scales { get; }
    public ObservableCollection<int> Intervals { get; }
    public ObservableCollection<string> Resolutions { get; }

    // ---- Interface ---------------------------------------------------------------

    [ObservableProperty] private LanguageOption? _selectedLanguage;
    [ObservableProperty] private ScaleOption? _selectedScale;
    [ObservableProperty] private string? _selectedResolution;
    [ObservableProperty] private bool _darkMode = true;
    [ObservableProperty] private bool _animationsEnabled = true;
    [ObservableProperty] private int _realtimeIntervalMs = 1000;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _notificationsEnabled = true;
    [ObservableProperty] private bool _showTemperatureWarnings = true;

    // ---- Segurança e privacidade --------------------------------------------------

    [ObservableProperty] private bool _createRestorePointBeforeChanges = true;
    [ObservableProperty] private bool _backupRegistryBeforeChanges = true;
    [ObservableProperty] private bool _confirmRiskyActions = true;
    [ObservableProperty] private bool _simulationMode;
    [ObservableProperty] private bool _telemetryEnabled;
    [ObservableProperty] private string _telemetryStateText = string.Empty;
    [ObservableProperty] private string _hasChangesText = string.Empty;

    // ---- Conta / perfil -----------------------------------------------------------

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _accountTypeText = string.Empty;
    [ObservableProperty] private string _licenseText = string.Empty;

    // ---- Sistema ------------------------------------------------------------------

    [ObservableProperty] private string _systemSummary = string.Empty;
    [ObservableProperty] private string _screenInfoText = string.Empty;
    [ObservableProperty] private string _dataFolderText = string.Empty;
    [ObservableProperty] private string _storageText = string.Empty;

    /// <summary>Histórico de atividades (ações aplicadas, limpezas, erros).</summary>
    public ReadOnlyObservableCollection<ActivityEntry> History => _activity.Entries;

    private void LoadFromSettings()
    {
        var current = _settings.Current;

        _darkMode = current.DarkMode;
        _animationsEnabled = current.AnimationsEnabled;
        _realtimeIntervalMs = current.RealtimeIntervalMs;
        _startWithWindows = current.StartWithWindows;
        _startMinimized = current.StartMinimized;
        _minimizeToTray = current.MinimizeToTray;
        _notificationsEnabled = current.NotificationsEnabled;
        _showTemperatureWarnings = current.ShowTemperatureWarnings;
        _createRestorePointBeforeChanges = current.CreateRestorePointBeforeChanges;
        _backupRegistryBeforeChanges = current.BackupRegistryBeforeChanges;
        _confirmRiskyActions = current.ConfirmRiskyActions;
        _simulationMode = current.SimulationMode;
        _telemetryEnabled = current.TelemetryEnabled;

        DisplayName = _accounts.Current.DisplayName;
        Email = string.IsNullOrWhiteSpace(_accounts.Current.Email) ? "(sessão local, sem e-mail)" : _accounts.Current.Email;
        AccountTypeText = _accounts.Current.IsMicrosoftAccount
            ? "Conta Microsoft"
            : _accounts.Current.IsLocalOnly ? "Sessão local (sem conta)" : "Conta local";
        LicenseText = $"{_license.Current.TierText} — {_license.Current.ExpiryText}";

        UpdateTelemetryText();
        UpdateScreenInfo();
        DetectSystemInternal();
        UpdateStorageText();
    }

    private void UpdateTelemetryText()
    {
        TelemetryStateText = TelemetryEnabled
            ? $"Telemetria ATIVADA (consentimento em " +
              $"{(_settings.Current.TelemetryConsentDate?.ToString("dd/MM/yyyy") ?? "data desconhecida")}). " +
              "São enviados apenas dados anónimos de desempenho — nunca ficheiros, nomes ou conteúdos."
            : "Telemetria DESLIGADA (predefinição). Nenhum dado sai deste computador.";
    }

    private void UpdateScreenInfo()
    {
        var (width, height, dpi) = _theme.GetScreenInfo();
        ScreenInfoText = $"{width} × {height} px • {dpi:0} DPI • escala atual {_theme.Scale * 100:0} %";
    }

    private void DetectSystemInternal()
    {
        _systemInfo.Refresh();
        SystemSummary = _systemInfo.Hardware.Summary;
    }

    private void UpdateStorageText()
    {
        DataFolderText = AppPaths.Root;

        try
        {
            var size = new DirectoryInfo(AppPaths.Root).EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            StorageText = $"Dados da aplicação: {Formatters.Bytes(size)} em {AppPaths.Root}";
        }
        catch (Exception ex)
        {
            StorageText = $"Não foi possível medir o tamanho da pasta de dados ({ex.Message}).";
        }
    }

    // ---- Ações --------------------------------------------------------------------

    /// <summary>Guarda as preferências e aplica o que é aplicável de imediato.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        await RunBusyAsync(() =>
        {
            _settings.Update(s =>
            {
                if (SelectedLanguage is not null) s.Language = SelectedLanguage.Code;
                if (SelectedScale is not null) s.UiScale = SelectedScale.Value;
                if (SelectedResolution is not null) s.PreferredResolution = SelectedResolution.StartsWith("Auto") ? "Auto" : SelectedResolution;

                s.DarkMode = DarkMode;
                s.AnimationsEnabled = AnimationsEnabled;
                s.RealtimeIntervalMs = RealtimeIntervalMs;
                s.StartWithWindows = StartWithWindows;
                s.StartMinimized = StartMinimized;
                s.MinimizeToTray = MinimizeToTray;
                s.NotificationsEnabled = NotificationsEnabled;
                s.ShowTemperatureWarnings = ShowTemperatureWarnings;
                s.CreateRestorePointBeforeChanges = CreateRestorePointBeforeChanges;
                s.BackupRegistryBeforeChanges = BackupRegistryBeforeChanges;
                s.ConfirmRiskyActions = ConfirmRiskyActions;
                s.SimulationMode = SimulationMode;
                s.TelemetryEnabled = TelemetryEnabled;
            });

            // Iniciar com o Windows: escreve/remove a entrada no registo (reversível).
            var startupResult = StartupRegistration.Apply(StartWithWindows);

            // Idioma (aplica-se imediatamente aos textos gerados em código).
            if (SelectedLanguage is not null && SelectedLanguage.Code != _localization.CurrentLanguage)
                _localization.SetLanguage(SelectedLanguage.Code);

            // Escala da interface.
            if (SelectedScale is not null && System.Windows.Application.Current?.MainWindow is { } window)
                _theme.ApplyScale(window, SelectedScale.Value);

            if (SelectedLanguage is not null && SelectedLanguage.Code != _localization.CurrentLanguage)
                Notifications.Info("Idioma alterado", "Alguns textos já desenhados precisam de reiniciar a aplicação para atualizar.");

            Report(startupResult, "Configuração");
            SetStatus("Configurações guardadas com sucesso.");
            UpdateTelemetryText();
            UpdateScreenInfo();
            UpdateStorageText();
            return Task.CompletedTask;
        }, "A guardar as configurações…");
    }

    /// <summary>Deteta novamente o hardware e o sistema (limpa a cache do serviço).</summary>
    [RelayCommand]
    private void DetectSystem()
    {
        DetectSystemInternal();
        UpdateScreenInfo();
        SetStatus("Hardware e sistema detetados novamente.");
    }

    [RelayCommand]
    private void OpenDataFolder() => _quickActions.OpenDataFolder();

    [RelayCommand]
    private async Task ExportHistoryAsync()
    {
        var destination = _dialogs.PickSaveFile("Ficheiro CSV (*.csv)|*.csv",
            $"hl-optimizer-historico-{DateTime.Now:yyyyMMdd}.csv", "Exportar histórico");

        if (string.IsNullOrWhiteSpace(destination)) return;

        Report(_activity.Export(destination), "Histórico");

        if (File.Exists(destination)) _dialogs.OpenInShell(destination);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Limpar histórico",
            "O histórico de atividades e as amostras de desempenho das últimas 24 h vão ser apagados.\n\n" +
            "As otimizações aplicadas NÃO são revertidas — para isso use «Otimizações → Reverter tudo».",
            "Limpar", danger: true);

        if (!confirmed) return;

        Report(_activity.Clear(), "Histórico");
        Report(_history.Clear(), "Histórico de desempenho");
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Restaurar predefinições",
            "Todas as preferências voltam aos valores originais. " +
            "As otimizações aplicadas no sistema e o diário de alterações mantêm-se intactos.",
            "Restaurar");

        if (!confirmed) return;

        _settings.ResetToDefaults();
        LoadFromSettings();
        SetStatus("Preferências restauradas para os valores de origem.");
        Notifications.Info("Configuração", "Preferências restauradas.");
    }

    /// <summary>Termina a sessão e volta ao ecrã de boas-vindas.</summary>
    [RelayCommand]
    private async Task SignOutAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Terminar sessão",
            "Vai sair da conta atual. As otimizações aplicadas mantêm-se e o diário de alterações fica guardado.",
            "Terminar sessão");

        if (!confirmed) return;

        var deleteData = await _dialogs.ConfirmAsync(
            "Dados locais",
            "Quer também apagar os dados locais da conta (histórico, preferências e licença guardada neste computador)?\n\n" +
            "Recomendamos «Cancelar» se pretende voltar a entrar mais tarde.",
            "Apagar dados locais", danger: true);

        Report(_accounts.SignOut(deleteData), "Conta");

        // Reinicia a aplicação para mostrar o ecrã de boas-vindas no estado correto.
        var restart = await _dialogs.ConfirmAsync(
            "Reiniciar a aplicação",
            "Para concluir, a aplicação vai reiniciar. Continuar?",
            "Reiniciar agora");

        if (restart)
        {
            System.Diagnostics.Process.Start(Environment.ProcessPath!);
            System.Windows.Application.Current.Shutdown();
        }
    }

    /// <summary>Sai da aplicação guardando tudo.</summary>
    [RelayCommand]
    private async Task ExitApplicationAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Sair do HL Optimizer Pro",
            "As preferências e o histórico são guardados. Se o Modo Jogo estiver ativo, as prioridades " +
            "e o plano de energia são repostos antes de fechar.",
            "Sair");

        if (!confirmed) return;

        if (System.Windows.Application.Current is App app)
            app.RequestShutdown();
    }

    partial void OnTelemetryEnabledChanged(bool value)
    {
        if (value && _settings.Current.TelemetryConsentDate is null)
        {
            // Consentimento explícito: registamos a data apenas quando o utilizador ativa.
            _settings.Update(s => s.TelemetryConsentDate = DateTime.Now);
        }

        UpdateTelemetryText();
    }

    partial void OnSimulationModeChanged(bool value) => HasChangesText = "Alterações por guardar.";
}

/// <summary>
/// Entrada "Iniciar com o Windows" no registo do utilizador (HKCU), reversível.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HLOptimizerPro";

    /// <summary>Liga/desliga o arranque automático. Devolve o resultado explicado.</summary>
    public static Result Apply(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);

            if (key is null)
                return Result.Fail("Não foi possível abrir a chave de arranque do Windows.");

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(path))
                    return Result.Fail("Não foi possível determinar o caminho do executável.");

                key.SetValue(ValueName, $"\"{path}\" --tray");
                return Result.Ok("O HL Optimizer Pro vai iniciar com o Windows (pode desligar aqui a qualquer momento).");
            }

            if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);

            return Result.Ok("O HL Optimizer Pro já não inicia com o Windows.");
        }
        catch (Exception ex)
        {
            return Result.Fail("Falha ao configurar o arranque automático.", ex.Message);
        }
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using HLOptimizerPro.Core.Services.Tweaks;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>Um separador das Otimizações do Windows (Windows, Sistema, Privacidade, ...).</summary>
public sealed partial class OptimizationTab : ObservableObject
{
    public OptimizationTab(OptimizationCategory category, string name, string iconKind)
    {
        Category = category;
        Name = name;
        IconKind = iconKind;
    }

    public OptimizationCategory Category { get; }
    public string Name { get; }
    public string IconKind { get; }

    /// <summary>Ações deste separador (com o estado já resolvido).</summary>
    public ObservableCollection<OptimizationAction> Actions { get; } = new();

    [ObservableProperty] private bool _isSelected;

    public int Count => Actions.Count;
    public int AppliedCount => Actions.Count(a => OptimizationCatalog.IsApplied(a));

    public string Summary => Actions.Count == 0
        ? "Sem ajustes disponíveis nesta categoria."
        : $"{AppliedCount} de {Count} ajustes aplicados";
}

/// <summary>
/// ViewModel das Otimizações do Windows (3.5).
///
/// Estrutura pedida no briefing: separadores Windows / Sistema / Privacidade / Unidade de Disco /
/// Windows Update / Toque / Aplicativos. Cada ajuste mostra descrição, o que muda, o risco e se é
/// reversível — e todos os ajustes passam pelo motor (ponto de restauro + backup + diário).
///
/// Inclui também a limpeza automática (ligar/desligar + intervalo em dias + configuração dos alvos).
/// </summary>
public sealed partial class OptimizationsViewModel : ViewModelBase
{
    private readonly OptimizationCatalog _catalog;
    private readonly OptimizationEngine _engine;
    private readonly OptimizationJournal _journal;
    private readonly AutoCleanupService _autoCleanup;
    private readonly IReadOnlyList<CleanupTarget> _cleanupTargetsCatalog;
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;

    public OptimizationsViewModel(
        NotificationService notifications,
        OptimizationCatalog catalog,
        OptimizationEngine engine,
        OptimizationJournal journal,
        AutoCleanupService autoCleanup,
        SettingsService settings,
        IDialogService dialogs,
        INavigationService navigation)
        : base(notifications)
    {
        _catalog = catalog;
        _engine = engine;
        _journal = journal;
        _autoCleanup = autoCleanup;
        _cleanupTargetsCatalog = Services.CleanupTargets.All();
        _settings = settings;
        _dialogs = dialogs;
        _navigation = navigation;

        Title = "Otimizações do Windows";
        Subtitle = "Ajustes reversíveis, explicados e com backup antes de qualquer alteração";
        IconKind = "RocketLaunch";

        Tabs.Add(new OptimizationTab(OptimizationCategory.Windows, "Windows", "MicrosoftWindows"));
        Tabs.Add(new OptimizationTab(OptimizationCategory.Sistema, "Sistema", "Cog"));
        Tabs.Add(new OptimizationTab(OptimizationCategory.Privacidade, "Privacidade", "ShieldLock"));
        Tabs.Add(new OptimizationTab(OptimizationCategory.UnidadeDisco, "Unidade de Disco", "Harddisk"));
        Tabs.Add(new OptimizationTab(OptimizationCategory.WindowsUpdate, "Windows Update", "Update"));
        Tabs.Add(new OptimizationTab(OptimizationCategory.Toque, "Toque", "GestureTap"));
        Tabs.Add(new OptimizationTab(OptimizationCategory.Aplicativos, "Aplicativos", "Apps"));

        SelectedTab = Tabs.First();

        AutoCleanupEnabled = settings.Current.AutoCleanupEnabled;
        AutoCleanupIntervalDays = settings.Current.AutoCleanupIntervalDays;
        LoadCleanupTargets();
        UpdateAutoCleanupDescription();
    }

    // ---- Separadores ----------------------------------------------------------

    public ObservableCollection<OptimizationTab> Tabs { get; } = new();

    [ObservableProperty] private OptimizationTab? _selectedTab;
    [ObservableProperty] private string _coverageText = string.Empty;
    [ObservableProperty] private string _lastRunText = "Ainda não aplicou otimizações nesta sessão.";
    [ObservableProperty] private bool _onlyPending;

    /// <summary>Ações do separador atual, filtradas (pendentes/aplicadas).</summary>
    public IEnumerable<OptimizationAction> VisibleActions => SelectedTab is null
        ? Enumerable.Empty<OptimizationAction>()
        : OnlyPending
            ? SelectedTab.Actions.Where(a => !OptimizationCatalog.IsApplied(a))
            : SelectedTab.Actions;

    /// <summary>Seleciona um separador (clique no separador ou navegação interna).</summary>
    [RelayCommand]
    private void SelectTab(OptimizationTab? tab)
    {
        if (tab is null) return;

        SelectedTab = tab;

        foreach (var item in Tabs)
            item.IsSelected = ReferenceEquals(item, tab);
    }

    partial void OnSelectedTabChanged(OptimizationTab? value) => OnPropertyChanged(nameof(VisibleActions));

    partial void OnOnlyPendingChanged(bool value) => OnPropertyChanged(nameof(VisibleActions));

    // ---- Limpeza automática ---------------------------------------------------

    [ObservableProperty] private bool _autoCleanupEnabled;
    [ObservableProperty] private int _autoCleanupIntervalDays;
    [ObservableProperty] private string _autoCleanupDescription = string.Empty;
    [ObservableProperty] private string _autoCleanupNextRun = string.Empty;

    public ObservableCollection<CleanupTargetItem> CleanupTargets { get; } = new();

    /// <summary>Intervalos apresentados na Configuração (dias).</summary>
    public IReadOnlyList<int> IntervalOptions { get; } = new[] { 1, 3, 7, 14, 30 };

    public sealed partial class CleanupTargetItem : ObservableObject
    {
        public CleanupTargetItem(CleanupTarget target)
        {
            Target = target;
            _isSelected = target.SelectedByDefault;
        }

        public CleanupTarget Target { get; }
        public string Key => Target.Key;
        public string Name => Target.Name;
        public string Description => Target.Description;
        public string IconKind => Target.IconKind;

        [ObservableProperty] private bool _isSelected;
    }

    private void LoadCleanupTargets()
    {
        CleanupTargets.Clear();

        var configured = _settings.Current.AutoCleanupTargets;

        foreach (var target in _cleanupTargetsCatalog)
        {
            CleanupTargets.Add(new CleanupTargetItem(target)
            {
                IsSelected = configured.Count == 0 ? target.SelectedByDefault : configured.Contains(target.Key),
            });
        }
    }

    private void UpdateAutoCleanupDescription()
    {
        AutoCleanupDescription = _autoCleanup.Describe();

        AutoCleanupNextRun = _autoCleanup.NextRun is { } next
            ? $"Próxima execução: {next:dd/MM/yyyy HH:mm}" +
              (AutoCleanupEnabled ? string.Empty : " (limpeza automática desligada)")
            : "Nenhuma execução agendada (limpeza automática desligada).";
    }

    [RelayCommand]
    private async Task ConfigureAutoCleanupAsync()
    {
        var selected = CleanupTargets.Where(t => t.IsSelected).Select(t => t.Key).ToList();

        if (selected.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Sem alvos",
                "Escolha pelo menos um tipo de limpeza para a limpeza automática.");
            return;
        }

        var result = _autoCleanup.Configure(AutoCleanupEnabled, AutoCleanupIntervalDays, selected);
        _settings.Update(s =>
        {
            s.AutoCleanupEnabled = AutoCleanupEnabled;
            s.AutoCleanupIntervalDays = AutoCleanupIntervalDays;
        });

        Report(result, "Limpeza automática");
        UpdateAutoCleanupDescription();
    }

    [RelayCommand]
    private async Task RunAutoCleanupNowAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _autoCleanup.RunNowAsync();
            Report(result, "Limpeza automática");
            UpdateAutoCleanupDescription();
        }, "A executar a limpeza automática…");
    }

    // ---- Aplicar otimizações --------------------------------------------------

    [RelayCommand]
    private async Task ApplyAllAsync()
    {
        var pending = _catalog.All.Where(a => a.IsAvailable && !OptimizationCatalog.IsApplied(a)).ToList();

        if (pending.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Tudo aplicado",
                "Não existem otimizações pendentes — o sistema já está otimizado nesta categoria.");
            return;
        }

        await ApplyWithConfirmationAsync(pending, "Otimizações do Windows");
    }

    private async Task ApplyWithConfirmationAsync(IReadOnlyList<OptimizationAction> actions, string batchName)
    {
        var simulation = _catalog.SimulationMode;
        var risky = actions.Where(a => a.RequiresConfirmation || a.RiskLevel != HealthLevel.Otimo).ToList();

        var message =
            $"{actions.Count} otimizações vão ser aplicadas:\n\n" +
            string.Join("\n", actions.Take(12).Select(a => $"• {a.Title} — {a.Risk}")) +
            (actions.Count > 12 ? $"\n… e mais {actions.Count - 12}." : string.Empty) +
            "\n\nAntes de escrever: " +
            (_settings.Current.CreateRestorePointBeforeChanges
                ? "ponto de restauro automático"
                : "sem ponto de restauro (desativado na Configuração)") +
            " + backup dos valores originais no diário de alterações (reversão a 100 %).";

        var confirmed = await _dialogs.ConfirmAsync(
            batchName,
            message,
            "Aplicar otimizações",
            danger: risky.Count > 0);

        if (!confirmed) return;

        await RunBusyAsync(async () =>
        {
            _engine.ProgressChanged += OnEngineProgress;

            try
            {
                var result = await _engine.ApplyAsync(actions, new OptimizationRunOptions
                {
                    BatchName = batchName,
                    Simulation = simulation,
                    CreateRestorePoint = _settings.Current.CreateRestorePointBeforeChanges,
                    ActivityCategory = "Otimizações",
                });

                if (simulation)
                    await _dialogs.ShowSimulationAsync("Modo Simular — otimizações", _engine.Preview(actions));

                Report(result, batchName);

                LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
                Refresh();
            }
            finally
            {
                _engine.ProgressChanged -= OnEngineProgress;
            }
        }, $"A aplicar {batchName}…");
    }

    private void OnEngineProgress(object? sender, OptimizationProgress progress)
        => BusyMessage = $"[{progress.Index}/{progress.Total}] {progress.ActionTitle}";

    /// <summary>Aplica um único ajuste (botão «Aplicar» no cartão).</summary>
    [RelayCommand]
    private async Task ApplySingleAsync(OptimizationAction? action)
    {
        if (action is null) return;

        var simulation = _catalog.SimulationMode;

        if (action.RequiresConfirmation && _settings.Current.ConfirmRiskyActions && !simulation)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                action.Title,
                $"{action.Description}\n\n" +
                $"O que muda: {action.Target}\n" +
                $"Agora: {action.CurrentValue}\n" +
                $"Depois: {action.NewValue}\n\n" +
                $"Risco: {action.Risk}\n\n" +
                "O valor original fica guardado e pode reverter este ajuste em «A restaurar».",
                "Aplicar", danger: true);

            if (!confirmed) return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _engine.ApplyAsync(new[] { action }, new OptimizationRunOptions
            {
                BatchName = action.Title,
                Simulation = simulation,
                CreateRestorePoint = _settings.Current.CreateRestorePointBeforeChanges && action.RequiresRestart,
                ActivityCategory = "Otimizações",
            });

            if (simulation)
                await _dialogs.ShowSimulationAsync("Modo Simular", new[] { action.SimulationText });

            Report(result, "Otimizações");
            LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
            Refresh();
        }, $"A aplicar {action.Title}…");
    }

    /// <summary>Mostra no diálogo exatamente o que seria alterado (modo Simular por ajuste).</summary>
    [RelayCommand]
    private async Task PreviewAsync(OptimizationAction? action)
    {
        if (action is null) return;

        await _dialogs.ShowSimulationAsync(
            $"Modo Simular — {action.Title}",
            new[]
            {
                action.SimulationText,
                $"Alvo: {action.Target}",
                $"Valor atual: {action.CurrentValue}",
                $"Valor novo: {action.NewValue}",
                $"Risco: {action.Risk}",
                action.RestartText,
            });
    }

    // ---- Reversão --------------------------------------------------------------

    [RelayCommand]
    private async Task RevertAllAsync()
    {
        var active = _journal.ActiveEntries;
        if (active.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Nada para reverter",
                "Não existem alterações ativas registadas no diário.");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Reverter todas as otimizações",
            $"Repor os valores originais de {active.Count} alteração(ões)?\n\n" +
            string.Join("\n", active.Take(15).Select(e => $"• {e.Title} (aplicado {e.AppliedAt:dd/MM HH:mm})")) +
            (active.Count > 15 ? $"\n… e mais {active.Count - 15}." : string.Empty),
            "Reverter tudo", danger: true);

        if (!confirmed) return;

        await RunBusyAsync(async () =>
        {
            var result = await _engine.RevertAllAsync();
            Report(result, "Otimizações");
            LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
            Refresh();
        }, "A reverter as otimizações…");
    }

    [RelayCommand]
    private async Task ExportJournalAsync()
    {
        var destination = _dialogs.PickSaveFile("Ficheiro CSV (*.csv)|*.csv",
            $"hl-optimizer-alteracoes-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            "Exportar diário de alterações");

        if (string.IsNullOrWhiteSpace(destination)) return;

        var result = _journal.Export(destination);
        Report(result, "Diário de alterações");

        if (result.Success) _dialogs.OpenInShell(destination);
    }

    /// <summary>Vai para a página onde se veem as alterações a restaurar.</summary>
    [RelayCommand]
    private async Task GoToJournalAsync()
        => await _navigation.NavigateToAsync(NavigationKeys.Recommendations);

    // ---- Atualização ------------------------------------------------------------

    [RelayCommand]
    private void Refresh()
    {
        _catalog.Refresh();

        foreach (var tab in Tabs)
        {
            tab.Actions.Clear();

            foreach (var action in _catalog.ByCategory(tab.Category))
                tab.Actions.Add(action);

            tab.IsSelected = ReferenceEquals(tab, SelectedTab);
        }

        var total = _catalog.All.Count(a => a.IsAvailable);
        var applied = _catalog.All.Count(a => a.IsAvailable && OptimizationCatalog.IsApplied(a));

        CoverageText = $"{applied} de {total} otimizações aplicadas neste computador • " +
                       $"{_journal.ActiveCount} alterações ativas no diário";

        OnPropertyChanged(nameof(VisibleActions));
        UpdateAutoCleanupDescription();
    }

    public override async Task OnNavigatedToAsync(object? parameter = null)
    {
        await base.OnNavigatedToAsync(parameter);
        Refresh();
    }
}

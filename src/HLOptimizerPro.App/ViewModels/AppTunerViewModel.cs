using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using HLOptimizerPro.Core.Services.Tweaks;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>
/// Linha de uma aplicação no App Tuner.
///
/// Cada linha permite escolher a prioridade de CPU (tempo real → baixa), a afinidade de núcleos e
/// o plano de energia a aplicar quando a aplicação estiver em execução. Nada é aplicado até o
/// utilizador carregar em «Otimizar Selecionados».
/// </summary>
public sealed partial class AppTunerRow : ObservableObject
{
    public AppTunerRow(AppOptimizationProfile profile, AppDetectionState state, bool isRunning, int runningPid)
    {
        Profile = profile;
        State = state;
        IsRunning = isRunning;
        RunningPid = runningPid;
        PriorityName = profile.ProcessPriority;
        _isSelected = state == AppDetectionState.Instalado && profile.IsGame == false;
    }

    public AppOptimizationProfile Profile { get; }

    public string AppKey => Profile.AppKey;
    public string DisplayName => Profile.DisplayName;
    public string IconGlyph => Profile.IconGlyph;
    public string Notes => Profile.Notes;
    public string ExpectedGain => Profile.ExpectedGain;
    public string ProcessNamesText => string.Join(", ", Profile.ProcessNames);

    [ObservableProperty] private AppDetectionState _state;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _runningPid;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _priorityName;
    [ObservableProperty] private bool _useAffinity = true;
    [ObservableProperty] private bool _applyPowerPlan = true;

    public bool IsInstalled => State is AppDetectionState.Instalado or AppDetectionState.Otimizado;

    /// <summary>Estado mostrado na lista: Otimizado / Pronto / Não instalado.</summary>
    public string StateText => State switch
    {
        AppDetectionState.Otimizado => "Otimizado",
        AppDetectionState.Instalado => "Pronto",
        AppDetectionState.NaoInstalado => "Não instalado",
        _ => "Desconhecido",
    };

    public HealthLevel StateHealth => State switch
    {
        AppDetectionState.Otimizado => HealthLevel.Otimo,
        AppDetectionState.Instalado => HealthLevel.Atencao,
        AppDetectionState.NaoInstalado => HealthLevel.Desconhecido,
        _ => HealthLevel.Desconhecido,
    };

    public string RunningText => IsRunning ? $"Em execução (PID {RunningPid})" : "Não está em execução";

    public string AffinityText => UseAffinity
        ? $"{Profile.PreferredCores} núcleos preferidos"
        : "todos os núcleos";

    public string PowerPlanText => ApplyPowerPlan ? "Plano de alto desempenho" : "Manter plano atual";

    partial void OnStateChanged(AppDetectionState value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateHealth));
        OnPropertyChanged(nameof(IsInstalled));
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(RunningText));

    partial void OnUseAffinityChanged(bool value)
    {
        Profile.UseCpuAffinity = value;
        OnPropertyChanged(nameof(AffinityText));
    }

    partial void OnApplyPowerPlanChanged(bool value)
    {
        Profile.ApplyPowerPlan = value;
        OnPropertyChanged(nameof(PowerPlanText));
    }

    partial void OnPriorityNameChanged(string value) => Profile.ProcessPriority = value;
}

/// <summary>
/// ViewModel do App Tuner (3.4-c).
///
/// O utilizador escolhe as aplicações e a forma de otimização; ao aplicar, cada ajuste passa pelo
/// <see cref="OptimizationEngine"/>, que guarda o estado original (prioridade, afinidade e plano de
/// energia) e grava tudo no diário. A reversão repõe exatamente os valores anteriores.
/// </summary>
public sealed partial class AppTunerViewModel : ViewModelBase
{
    private readonly AppTunerService _appTuner;
    private readonly OptimizationEngine _engine;
    private readonly OptimizationCatalog _catalog;
    private readonly OptimizationJournal _journal;
    private readonly IDialogService _dialogs;

    public AppTunerViewModel(
        NotificationService notifications,
        AppTunerService appTuner,
        OptimizationEngine engine,
        OptimizationCatalog catalog,
        OptimizationJournal journal,
        IDialogService dialogs)
        : base(notifications)
    {
        _appTuner = appTuner;
        _engine = engine;
        _catalog = catalog;
        _journal = journal;
        _dialogs = dialogs;

        Title = "App Tuner";
        Subtitle = "Prioridade, afinidade e energia por aplicação — com reversão total";
        IconKind = "Apps";

        Load();
    }

    // ---- Estado ---------------------------------------------------------------

    [ObservableProperty] private string _summaryText = "A detetar aplicações…";
    [ObservableProperty] private bool _showAll = true;
    [ObservableProperty] private string _selectedCountText = string.Empty;
    [ObservableProperty] private string _lastRunText = "Ainda não aplicou otimizações nesta sessão.";
    [ObservableProperty] private bool _hasImmediateAvailable;

    public ObservableCollection<AppTunerRow> Rows { get; } = new();

    /// <summary>Prioridades permitidas pelo Windows (ordem apresentada na lista).</summary>
    public IReadOnlyList<string> Priorities => ProcessOptimizationService.AllowedPriorities;

    public int AppliedCount => _journal.ActiveCount;

    // ---- Deteção --------------------------------------------------------------

    [RelayCommand]
    private void Detect()
    {
        Load();
        SetStatus($"{Rows.Count(r => r.IsInstalled)} aplicações detetadas neste computador.");
    }

    private void Load()
    {
        var detected = _appTuner.DetectApplications();

        Rows.Clear();
        foreach (var profile in detected)
        {
            var isRunning = profile.RunningPid > 0;
            Rows.Add(new AppTunerRow(profile, profile.State, isRunning, profile.RunningPid));
        }

        UpdateCoverage();
    }

    private void UpdateCoverage()
    {
        var installed = Rows.Count(r => r.IsInstalled);
        var optimized = Rows.Count(r => r.State == AppDetectionState.Otimizado);
        var running = Rows.Count(r => r.IsRunning);

        SummaryText = installed == 0
            ? "Nenhuma das aplicações populares foi encontrada neste computador. Ainda assim pode adicionar " +
              "um jogo manualmente no Modo Jogo."
            : $"{installed} aplicações instaladas • {optimized} já otimizadas • {running} em execução agora";

        SelectedCountText = $"{Rows.Count(r => r.IsSelected)} selecionada(s)";
        HasImmediateAvailable = Rows.Any(r => r.IsRunning && r.IsInstalled);
        OnPropertyChanged(nameof(AppliedCount));
    }

    partial void OnShowAllChanged(bool value) => UpdateCoverage();

    // ---- Aplicar --------------------------------------------------------------

    /// <summary>Aplica a tunelagem às aplicações selecionadas (passa pelo motor de otimizações).</summary>
    [RelayCommand]
    private async Task OptimizeSelectedAsync()
    {
        var selected = Rows.Where(r => r.IsSelected && r.IsInstalled).Select(r => r.Profile).ToList();

        if (selected.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Sem seleção",
                "Escolha pelo menos uma aplicação instalada para otimizar.");
            return;
        }

        var simulation = _catalog.SimulationMode;
        var plannedByPower = selected.Any(p => p.ApplyPowerPlan);

        var confirmed = await _dialogs.ConfirmAsync(
            "Otimizar aplicações",
            $"Serão otimizadas {selected.Count} aplicação(ões):\n\n" +
            string.Join("\n", selected.Select(p =>
                $"• {p.DisplayName} — prioridade {p.ProcessPriority}" +
                (p.UseCpuAffinity ? $", {p.PreferredCores} núcleos" : string.Empty) +
                (p.ApplyPowerPlan ? ", plano de alto desempenho" : string.Empty))) +
            (plannedByPower
                ? "\n\nNota: o plano de energia de alto desempenho só é aplicado quando a aplicação estiver em execução; " +
                  "ao terminar, o plano anterior é reposto."
                : string.Empty),
            "Otimizar");

        if (!confirmed) return;

        await RunBusyAsync(async () =>
        {
            var actions = _appTuner.CreateTuningActions(selected, simulation);

            var result = await _engine.ApplyAsync(actions, new OptimizationRunOptions
            {
                BatchName = "App Tuner",
                Simulation = simulation,
                CreateRestorePoint = false,   // são ajustes de processo/energia, imediatamente reversíveis
                ActivityCategory = "App Tuner",
            });

            if (simulation)
                await _dialogs.ShowSimulationAsync("Modo Simular — aplicações", _engine.Preview(actions));

            Report(result, "App Tuner");

            if (result.Success && !simulation)
            {
                foreach (var row in Rows.Where(r => r.IsSelected && r.IsInstalled))
                    row.State = AppDetectionState.Otimizado;
            }

            LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
            UpdateCoverage();
        }, "A otimizar aplicações…");
    }

    /// <summary>Aplica imediatamente o perfil a uma aplicação que está em execução.</summary>
    [RelayCommand]
    private async Task ApplyNowAsync(AppTunerRow? row)
    {
        if (row is null) return;

        if (!row.IsRunning)
        {
            await _dialogs.ShowInfoAsync("Aplicação fechada",
                $"«{row.DisplayName}» não está em execução. Abra-a primeiro para aplicar prioridade e afinidade " +
                "imediatamente — ou use «Otimizar Selecionados» para deixar a configuração memorizada.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _appTuner.ApplyImmediateAsync(row.Profile);
            Report(result, "App Tuner");

            if (result.Success) row.State = AppDetectionState.Otimizado;

            LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
        }, $"A otimizar {row.DisplayName}…");
    }

    /// <summary>Reversão de todas as otimizações aplicadas (prioridade, afinidade e planos).</summary>
    [RelayCommand]
    private async Task RevertAllAsync()
    {
        var active = _journal.ActiveEntries;
        if (active.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Nada para reverter",
                "Não existem otimizações aplicadas por esta aplicação que possam ser revertidas.");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Reverter otimizações",
            $"Vão ser repostas as configurações originais de {active.Count} otimização(ões):\n\n" +
            string.Join("\n", active.Take(12).Select(e => $"• {e.Title}")) +
            (active.Count > 12 ? $"\n… e mais {active.Count - 12}." : string.Empty),
            "Reverter tudo");

        if (!confirmed) return;

        await RunBusyAsync(async () =>
        {
            var result = await _engine.RevertAllAsync();
            Report(result, "App Tuner");

            if (result.Success)
            {
                foreach (var row in Rows)
                    row.State = row.IsInstalled ? AppDetectionState.Instalado : AppDetectionState.NaoInstalado;
            }

            LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
            UpdateCoverage();
        }, "A reverter otimizações…");
    }

    [RelayCommand]
    private void SelectInstalled()
    {
        foreach (var row in Rows) row.IsSelected = row.IsInstalled;
        UpdateCoverage();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Rows) row.IsSelected = false;
        UpdateCoverage();
    }
}

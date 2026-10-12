using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using HLOptimizerPro.Core.Services.Tweaks;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>App ou jogo popular mostrado no PC Boost (com o estado real de deteção).</summary>
public sealed partial class BoostTargetRow : ObservableObject
{
    public BoostTargetRow(AppOptimizationProfile profile)
    {
        Profile = profile;
        IsSelected = profile.IsInstalled;
    }

    public AppOptimizationProfile Profile { get; }

    public string DisplayName => Profile.DisplayName;
    public string IconGlyph => Profile.IconGlyph;
    public string Notes => Profile.Notes;
    public string ExpectedGain => Profile.ExpectedGain;
    public bool IsGame => Profile.IsGame;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isInstalled;

    public string StateText => IsInstalled ? "Detetado" : "Não encontrado";
    public HealthLevel StateHealth => IsInstalled ? HealthLevel.Otimo : HealthLevel.Desconhecido;
}

/// <summary>
/// ViewModel do PC Boost (3.6).
///
/// Pergunta ao utilizador para que usa o computador (Gamer ou Empresarial) e monta um plano com
/// base no hardware REALMENTE detetado — por exemplo, o plano Gamer só inclui ajustes de latência se
/// existir rede ativa; o Empresarial nunca altera definições que afetem redes corporativas, VPN ou
/// políticas de domínio. O plano é apresentado antes de ser aplicado.
/// </summary>
public sealed partial class PcBoostViewModel : ViewModelBase
{
    private readonly PerformanceBoostService _boost;
    private readonly OptimizationEngine _engine;
    private readonly OptimizationCatalog _catalog;
    private readonly AppTunerService _appTuner;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;

    public PcBoostViewModel(
        NotificationService notifications,
        PerformanceBoostService boost,
        OptimizationEngine engine,
        OptimizationCatalog catalog,
        AppTunerService appTuner,
        IDialogService dialogs,
        INavigationService navigation)
        : base(notifications)
    {
        _boost = boost;
        _engine = engine;
        _catalog = catalog;
        _appTuner = appTuner;
        _dialogs = dialogs;
        _navigation = navigation;

        Title = "PC Boost";
        Subtitle = "Vamos otimizar do jeito certo — com base no seu hardware";
        IconKind = "RocketLaunch";

        HardwareSummary = _boost.Hardware.Summary;

        foreach (var profile in _boost.GetQuickTargets())
            Targets.Add(new BoostTargetRow(profile));

        foreach (var row in Targets)
        {
            var detected = _appTuner.Detect(row.Profile);
            row.IsInstalled = detected.IsInstalled;
            row.IsSelected = detected.IsInstalled;
        }

        Usage = PrimaryUsage.Gamer;
        RefreshPlanPreview();
    }

    // ---- Hardware e escolha ----------------------------------------------------

    [ObservableProperty] private string _hardwareSummary = "A detetar hardware…";
    [ObservableProperty] private PrimaryUsage _usage;
    [ObservableProperty] private string _planDescription = string.Empty;
    [ObservableProperty] private string _impactText = string.Empty;
    [ObservableProperty] private string _planSummary = string.Empty;
    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private string _lastRunText = "Ainda não aplicou o plano PC Boost.";
    [ObservableProperty] private string _hardwareDetails = string.Empty;

    public ObservableCollection<BoostTargetRow> Targets { get; } = new();

    /// <summary>O que o plano faz (explicação antes de aplicar).</summary>
    public ObservableCollection<string> PlanReasons { get; } = new();

    public bool IsGamer
    {
        get => Usage == PrimaryUsage.Gamer;
        set
        {
            if (value) Usage = PrimaryUsage.Gamer;
        }
    }

    public bool IsBusiness
    {
        get => Usage == PrimaryUsage.Empresarial;
        set
        {
            if (value) Usage = PrimaryUsage.Empresarial;
        }
    }

    partial void OnUsageChanged(PrimaryUsage value)
    {
        OnPropertyChanged(nameof(IsGamer));
        OnPropertyChanged(nameof(IsBusiness));
        RefreshPlanPreview();
    }

    /// <summary>Recalcula o plano apresentado (nada é aplicado aqui).</summary>
    private void RefreshPlanPreview()
    {
        var plan = _boost.BuildPlan(Usage);

        PlanReasons.Clear();
        foreach (var reason in _boost.DescribePlan(Usage)) PlanReasons.Add(reason);

        PlanDescription = Usage switch
        {
            PrimaryUsage.Gamer =>
                "Um plano para jogar: prioridade ao jogo, latência reduzida e menos trabalho em segundo plano.",
            PrimaryUsage.Empresarial =>
                "Um plano para trabalhar: arranque rápido, interface responsiva e privacidade — sem tocar em VPN, domínios ou políticas da empresa.",
            _ => "Plano equilibrado.",
        };

        ImpactText = _boost.EstimateImpact(Usage, plan.Count);

        PlanSummary = plan.Count == 0
            ? "Não há otimizações aplicáveis neste computador."
            : $"{plan.Count} otimizações compõem este plano • " +
              $"{plan.Count(a => a.RequiresRestart)} exigem reinício • " +
              "todas reversíveis e com backup";

        HasPlan = plan.Count > 0;

        HardwareDetails = $"CPU: {_boost.Hardware.CpuName}\n" +
                          $"Núcleos: {_boost.Hardware.CpuThreads} threads\n" +
                          $"RAM: {_boost.Hardware.TotalRamBytes / 1024d / 1024d / 1024d:N1} GB\n" +
                          $"GPU: {_boost.Hardware.GpuName}\n" +
                          $"Armazenamento: {_boost.Hardware.StorageName} ({_boost.Hardware.StorageType})\n" +
                          $"SO: {_boost.Hardware.OperatingSystem} (build {_boost.Hardware.OsBuild})";
    }

    // ---- Aplicar ---------------------------------------------------------------

    /// <summary>Aplica o plano escolhido (com confirmação e resumo claro).</summary>
    [RelayCommand]
    private async Task ApplyPlanAsync()
    {
        var plan = _boost.BuildPlan(Usage);

        if (plan.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Sem plano", "Nenhuma otimização é aplicável a este computador.");
            return;
        }

        var confirmationMessage =
            $"Plano «{(Usage == PrimaryUsage.Gamer ? "Gamer" : "Empresarial")}» — {plan.Count} otimizações:\n\n" +
            string.Join("\n", plan.Take(14).Select(a => $"• {a.Title}")) +
            (plan.Count > 14 ? $"\n… e mais {plan.Count - 14}." : string.Empty) +
            "\n\n" + ImpactText +
            "\n\nVai ser criado um ponto de restauro e exportadas as chaves de registo afetadas.";

        var confirmed = await _dialogs.ConfirmAsync(
            "Aplicar plano PC Boost",
            confirmationMessage,
            "Aplicar plano",
            danger: Usage == PrimaryUsage.Gamer);

        if (!confirmed) return;

        var simulation = _catalog.SimulationMode;
        var appsToTune = Targets.Where(t => t.IsSelected && t.IsInstalled).Select(t => t.Profile).ToList();

        await RunBusyAsync(async () =>
        {
            _engine.ProgressChanged += OnEngineProgress;

            try
            {
                var actions = plan.ToList();

                // Junta a tunelagem das apps/jogos escolhidos pelo utilizador (mesma máquina, mesmo diário).
                if (appsToTune.Count > 0)
                    actions.AddRange(_appTuner.CreateTuningActions(appsToTune, simulation));

                var result = await _engine.ApplyAsync(actions, new OptimizationRunOptions
                {
                    BatchName = $"PC Boost {(Usage == PrimaryUsage.Gamer ? "Gamer" : "Empresarial")}",
                    Simulation = simulation,
                    CreateRestorePoint = true,
                    ActivityCategory = "PC Boost",
                });

                if (simulation)
                    await _dialogs.ShowSimulationAsync("Modo Simular — PC Boost", _engine.Preview(actions));

                Report(result, "PC Boost");

                LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
                _catalog.Refresh();
            }
            finally
            {
                _engine.ProgressChanged -= OnEngineProgress;
            }
        }, "A aplicar o plano PC Boost…");
    }

    private void OnEngineProgress(object? sender, OptimizationProgress progress)
        => BusyMessage = $"[{progress.Index}/{progress.Total}] {progress.ActionTitle}";

    /// <summary>Reposição completa: reverte tudo o que o PC Boost aplicou.</summary>
    [RelayCommand]
    private async Task RevertPlanAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Reverter PC Boost",
            "Repor todas as alterações feitas pela aplicação? O computador volta ao estado anterior.",
            "Reverter tudo", danger: true);

        if (!confirmed) return;

        await RunBusyAsync(async () =>
        {
            var result = await _engine.RevertAllAsync();
            Report(result, "PC Boost");
            LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
            _catalog.Refresh();
        }, "A reverter as alterações…");
    }

    [RelayCommand]
    private void DetectAgain()
    {
        foreach (var row in Targets)
        {
            var detected = _appTuner.Detect(row.Profile);
            row.IsInstalled = detected.IsInstalled;
            row.IsSelected = detected.IsInstalled;
        }

        RefreshPlanPreview();
        SetStatus($"{Targets.Count(t => t.IsInstalled)} de {Targets.Count} aplicações/jogos detetados.");
    }

    [RelayCommand]
    private async Task GoToOptimizationsAsync()
        => await _navigation.NavigateToAsync(NavigationKeys.Optimizations);

    public override async Task OnNavigatedToAsync(object? parameter = null)
    {
        await base.OnNavigatedToAsync(parameter);
        RefreshPlanPreview();
    }
}

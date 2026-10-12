using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using HLOptimizerPro.Core.Services.Tweaks;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>Cartão de um perfil na lista (guarda o estado de seleção para o destaque visual).</summary>
public sealed partial class TweakProfileCard : ObservableObject
{
    public TweakProfileCard(TweakProfileDefinition definition)
    {
        Definition = definition;
    }

    public TweakProfileDefinition Definition { get; }

    public TweakProfile Profile => Definition.Profile;
    public string Name => Definition.Name;
    public string Summary => Definition.Summary;
    public string IconKind => Definition.IconKind;
    public string RecommendedFor => Definition.RecommendedFor;
    public string Warning => Definition.Warning;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>Ajuste individual dentro de um perfil (usado no «Personalizar Avançado»).</summary>
public sealed partial class TweakSelectionItem : ObservableObject
{
    public TweakSelectionItem(OptimizationAction action)
    {
        Action = action;
    }

    public OptimizationAction Action { get; }

    public string Key => Action.Key;
    public string Title => Action.Title;
    public string Description => Action.Description;
    public string Target => Action.Target;
    public string Risk => Action.Risk;
    public HealthLevel RiskLevel => Action.RiskLevel;
    public string StatusText => Action.StatusText;
    public bool IsAvailable => Action.IsAvailable;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// ViewModel dos Tweaks por perfil (3.4-d).
///
/// Perfis: Competitivo, Streaming, Balanceado e Extremo. Cada perfil mostra exatamente o que vai ser
/// ajustado (lista do briefing) e as ações reais do catálogo que o compõem. O perfil Extremo avisa
/// do risco antes de aplicar. O «Personalizar Avançado» permite aplicar apenas alguns ajustes.
/// </summary>
public sealed partial class TweaksViewModel : ViewModelBase
{
    private readonly OptimizationCatalog _catalog;
    private readonly OptimizationEngine _engine;
    private readonly OptimizationJournal _journal;
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;

    public TweaksViewModel(
        NotificationService notifications,
        OptimizationCatalog catalog,
        OptimizationEngine engine,
        OptimizationJournal journal,
        IDialogService dialogs,
        SettingsService settings)
        : base(notifications)
    {
        _catalog = catalog;
        _engine = engine;
        _journal = journal;
        _dialogs = dialogs;
        _settings = settings;

        Title = "Tweaks";
        Subtitle = "Perfis prontos e personalização avançada, tudo reversível";
        IconKind = "Tune";

        foreach (var definition in TweakProfiles.All)
            Profiles.Add(new TweakProfileCard(definition));

        SelectProfileCommand.Execute(Profiles.FirstOrDefault());
    }

    // ---- Perfis ---------------------------------------------------------------

    public ObservableCollection<TweakProfileCard> Profiles { get; } = new();

    /// <summary>Escolhe o perfil a mostrar (destaque no cartão correspondente).</summary>
    [RelayCommand]
    private void SelectProfile(TweakProfileCard? card)
    {
        if (card is null) return;

        foreach (var item in Profiles)
            item.IsSelected = ReferenceEquals(item, card);

        SelectedProfile = card.Definition;
    }

    [ObservableProperty] private TweakProfileDefinition? _selectedProfile;
    [ObservableProperty] private bool _isAdvancedMode;
    [ObservableProperty] private string _lastRunText = "Ainda não aplicou nenhum perfil nesta sessão.";
    [ObservableProperty] private string _coverageText = string.Empty;

    /// <summary>Ajustes do perfil selecionado (para o modo avançado).</summary>
    public ObservableCollection<TweakSelectionItem> Adjustments { get; } = new();

    public string SelectedProfileWarning => SelectedProfile?.Warning ?? string.Empty;

    public bool HasWarning => !string.IsNullOrWhiteSpace(SelectedProfile?.Warning);

    public string SelectedProfileSummary => SelectedProfile?.Summary ?? string.Empty;

    public string SelectedProfileRecommendedFor => SelectedProfile is null
        ? string.Empty
        : $"Recomendado para: {SelectedProfile.RecommendedFor}";

    /// <summary>Lista textual do que o perfil ajusta (mostrada sempre, mesmo antes de aplicar).</summary>
    public string AdjustmentListText => SelectedProfile is null
        ? string.Empty
        : string.Join("\n", SelectedProfile.Adjustments.Select(a => $"• {a}"));

    partial void OnSelectedProfileChanged(TweakProfileDefinition? value)
    {
        OnPropertyChanged(nameof(SelectedProfileWarning));
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(SelectedProfileSummary));
        OnPropertyChanged(nameof(SelectedProfileRecommendedFor));
        OnPropertyChanged(nameof(AdjustmentListText));

        Adjustments.Clear();

        if (value is null) return;

        foreach (var action in _catalog.ByKeys(value.ActionKeys))
        {
            Adjustments.Add(new TweakSelectionItem(action)
            {
                IsSelected = true,   // por omissão aplicamos o perfil completo
            });
        }

        UpdateCoverage();
    }

    private void UpdateCoverage()
    {
        var total = Adjustments.Count;
        var applied = Adjustments.Count(a => OptimizationCatalog.IsApplied(a.Action));
        var unavailable = Adjustments.Count(a => !a.IsAvailable);

        CoverageText = total == 0
            ? "Este perfil não tem ajustes aplicáveis nesta versão do Windows."
            : $"{applied} de {total} ajustes já aplicados" +
              (unavailable > 0 ? $" • {unavailable} indisponível(is) neste sistema" : string.Empty);
    }

    // ---- Aplicar --------------------------------------------------------------

    /// <summary>Aplica o perfil selecionado na totalidade.</summary>
    [RelayCommand]
    private async Task ApplyProfileAsync()
    {
        if (SelectedProfile is null) return;

        var actions = _catalog.ByKeys(SelectedProfile.ActionKeys).Where(a => a.IsAvailable).ToList();

        if (actions.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Sem ajustes",
                "Nenhum ajuste deste perfil é aplicável a este computador.");
            return;
        }

        if (!await ConfirmProfileAsync(SelectedProfile, actions.Count)) return;

        await ApplyActionsAsync(actions, $"Perfil {SelectedProfile.Name}");
    }

    /// <summary>Aplica apenas os ajustes escolhidos no modo avançado.</summary>
    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        var actions = Adjustments
            .Where(a => a.IsSelected && a.IsAvailable)
            .Select(a => a.Action)
            .ToList();

        if (actions.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Sem ajustes selecionados",
                "Escolha pelo menos um ajuste na lista do modo avançado.");
            return;
        }

        if (!await ConfirmProfileAsync(SelectedProfile, actions.Count)) return;

        await ApplyActionsAsync(actions, "Tweaks personalizados");
    }

    /// <summary>Confirma com o utilizador, explicando riscos e reversão (obrigatório para ações de risco).</summary>
    private async Task<bool> ConfirmProfileAsync(TweakProfileDefinition? profile, int actionCount)
    {
        var risky = Adjustments.Count(a => a.IsSelected && (a.Action.RequiresConfirmation || a.Action.RiskLevel != HealthLevel.Otimo));
        var name = profile?.Name ?? "Tweaks personalizados";

        var message =
            $"{actionCount} ajustes vão ser aplicados ({name}):\n\n" +
            string.Join("\n", Adjustments.Where(a => a.IsSelected).Take(10).Select(a => $"• {a.Title} — {a.Risk}")) +
            (Adjustments.Count(a => a.IsSelected) > 10 ? "\n… (lista completa no ecrã)" : string.Empty) +
            "\n\nAntes de qualquer escrita: " +
            (_settings.Current.CreateRestorePointBeforeChanges
                ? "é criado um ponto de restauro; "
                : "não será criado ponto de restauro (opção desativada na Configuração); ") +
            (_settings.Current.BackupRegistryBeforeChanges
                ? "as chaves de registo afetadas são exportadas para .reg."
                : "o backup .reg está desativado na Configuração.") +
            (risky > 0 ? $"\n\nAtenção: {risky} ajuste(s) desta lista são de risco médio/alto." : string.Empty) +
            (profile is { Warning.Length: > 0 } ? $"\n\n{profile.Warning}" : string.Empty);

        return await _dialogs.ConfirmAsync(
            "Aplicar tweaks",
            message,
            "Aplicar",
            danger: profile?.Profile == TweakProfile.Extremo || risky > 0);
    }

    private async Task ApplyActionsAsync(IReadOnlyList<OptimizationAction> actions, string batchName)
    {
        var simulation = _catalog.SimulationMode;

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
                    ActivityCategory = "Tweaks",
                });

                if (simulation)
                    await _dialogs.ShowSimulationAsync("Modo Simular — tweaks", _engine.Preview(actions));

                Report(result, batchName);

                LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
                _catalog.Refresh();
                UpdateCoverage();
            }
            finally
            {
                _engine.ProgressChanged -= OnEngineProgress;
            }
        }, $"A aplicar {batchName}…");
    }

    private void OnEngineProgress(object? sender, OptimizationProgress progress)
        => BusyMessage = $"[{progress.Index}/{progress.Total}] {progress.ActionTitle}";

    /// <summary>Reverte os ajustes aplicados por esta página.</summary>
    [RelayCommand]
    private async Task RevertAsync()
    {
        var active = _journal.ActiveEntries;
        if (active.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Nada para reverter",
                "Não existem tweaks aplicados que possam ser revertidos.");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Reverter tweaks",
            $"Repor os valores originais de {active.Count} ajuste(s)?",
            "Reverter tudo");

        if (!confirmed) return;

        await RunBusyAsync(async () =>
        {
            var result = await _engine.RevertAllAsync();
            Report(result, "Tweaks");

            LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
            _catalog.Refresh();
            UpdateCoverage();
        }, "A reverter os tweaks…");
    }

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedMode = !IsAdvancedMode;

    [RelayCommand]
    private void SelectAllAdjustments()
    {
        foreach (var item in Adjustments) item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearAdjustments()
    {
        foreach (var item in Adjustments) item.IsSelected = false;
    }

    public override async Task OnNavigatedToAsync(object? parameter = null)
    {
        await base.OnNavigatedToAsync(parameter);
        _catalog.Refresh();
        UpdateCoverage();
    }
}

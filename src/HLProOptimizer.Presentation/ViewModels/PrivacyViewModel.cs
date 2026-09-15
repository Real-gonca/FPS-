using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Presentation.Controls;
using HLProOptimizer.Presentation.ViewModels.Items;
using LiveChartsCore;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// Tela "Privacidade": telemetria, rastreamento, aplicativos em segundo plano e
/// permissões — com score de proteção, aplicação seletiva e restauração de padrões.
/// </summary>
/// <remarks>
/// <para>
/// <b>Estado desejado vs. estado real.</b> <see cref="IPrivacyService.ApplyAsync"/>
/// lê <see cref="PrivacyItem.IsProtected"/> como o estado <i>desejado</i>. A tela marca
/// "aplicar" (<c>IsToApply</c>), grava o desejo no modelo e, após aplicar, recarrega os
/// itens do serviço — assim a UI nunca mente sobre o que o Windows realmente fez
/// (alguns itens exigem administrador e voltam como <c>Skipped</c>).
/// </para>
/// <para>
/// <b>Risco visível.</b> Itens com <c>RiskNote</c> exibem o aviso na própria linha:
/// desativar telemetria pode quebrar apps da Store, e o usuário precisa saber antes.
/// </para>
/// </remarks>
public sealed partial class PrivacyViewModel : ViewModelBase
{
    private readonly IPrivacyService _privacy;
    private readonly IDialogService _dialogs;
    private readonly IElevationService _elevation;
    private readonly IActionHistoryService _history;

    private readonly ObservableCollection<double> _protectionValue = [0];
    private readonly ObservableCollection<double> _protectionRemainder = [100];

    /// <summary>Cria o ViewModel de privacidade.</summary>
    /// <param name="privacy">Serviço de privacidade.</param>
    /// <param name="dialogs">Diálogos.</param>
    /// <param name="elevation">Elevação sob demanda.</param>
    /// <param name="history">Histórico de ações.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public PrivacyViewModel(
        IPrivacyService privacy,
        IDialogService dialogs,
        IElevationService elevation,
        IActionHistoryService history,
        ILocalizationService localization,
        ILogger<PrivacyViewModel> logger)
        : base(localization, logger)
    {
        _privacy = privacy;
        _dialogs = dialogs;
        _elevation = elevation;
        _history = history;

        IsElevated = _elevation.IsElevated;

        ProtectionSeries = ChartTheme.Gauge(_protectionValue, _protectionRemainder, ChartTheme.Success, innerRadius: 40, outerRadius: 54);
    }

    /// <summary>Anel do score de proteção.</summary>
    public ISeries[] ProtectionSeries { get; }

    /// <summary>Mantém o anel sincronizado com o score.</summary>
    partial void OnProtectionScoreChanged(double value) => OnUiThread(() =>
    {
        _protectionValue[0] = Math.Clamp(value, 0, 100);
        _protectionRemainder[0] = Math.Max(0, 100 - value);
    });

    /// <summary>Itens agrupados por categoria (abas da tela).</summary>
    public ObservableCollection<PrivacyGroupViewModel> Groups { get; } = [];

    /// <summary>Erros da última aplicação.</summary>
    public ObservableCollection<string> Errors { get; } = [];

    /// <summary>Score de proteção [0-100].</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProtectionScoreText))]
    private double _protectionScore;

    /// <summary>Texto do score de proteção.</summary>
    public string ProtectionScoreText => $"{ProtectionScore:F0}%";

    /// <summary>Total de itens monitorados.</summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>Itens já protegidos.</summary>
    [ObservableProperty]
    private int _protectedCount;

    /// <summary>Itens expostos.</summary>
    [ObservableProperty]
    private int _exposedCount;

    /// <summary>Itens marcados para aplicar.</summary>
    [ObservableProperty]
    private int _selectedCount;

    /// <summary>Indica se há itens carregados.</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>Indica se o processo é administrador.</summary>
    [ObservableProperty]
    private bool _isElevated;

    /// <summary>Indica se a última aplicação pediu elevação.</summary>
    [ObservableProperty]
    private bool _requiresElevation;

    /// <summary>Indica se há erros para exibir.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>Carrega os itens de privacidade.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var items = await _privacy.GetItemsAsync().ConfigureAwait(true);

            ApplyItems(items);

            IsLoaded = true;
            IsElevated = _elevation.IsElevated;

            StatusMessage = LF("Privacy_LoadedSummary", TotalCount, ProtectedCount);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Aplica os itens marcados.</summary>
    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var selected = Groups.SelectMany(g => g.Items).Where(i => i.IsToApply).ToList();

        if (selected.Count == 0)
        {
            await _dialogs.ShowInfoAsync(L("Common_Apply"), L("Privacy_NoItemSelected")).ConfigureAwait(true);
            return;
        }

        var risky = selected.Where(i => i.HasRiskNote).ToList();

        if (risky.Count > 0)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                L("Privacy_RiskConfirmTitle"),
                LF("Privacy_RiskConfirmMessage", risky.Count, string.Join(" · ", risky.Take(3).Select(r => r.DisplayName))),
                confirmText: L("Common_Apply"),
                isDestructive: true).ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }
        }

        await ApplyAsync(selected, L("Privacy_ApplySelected")).ConfigureAwait(true);
    }

    /// <summary>Aplica apenas as recomendações do produto.</summary>
    [RelayCommand]
    private async Task ApplyRecommendationsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            L("Privacy_ApplyRecommendations"),
            L("Privacy_ApplyRecommendationsConfirm")).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _privacy.ApplyRecommendationsAsync().ConfigureAwait(true);

            await ReportResultAsync(result, L("Privacy_ApplyRecommendations")).ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Restaura os padrões do Windows para todos os itens reversíveis.</summary>
    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            L("Privacy_RestoreDefaults"),
            L("Privacy_RestoreConfirm"),
            confirmText: L("Privacy_RestoreDefaults"),
            isDestructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _privacy.RestoreDefaultsAsync().ConfigureAwait(true);

            await ReportResultAsync(result, L("Privacy_RestoreDefaults")).ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Marca os itens recomendados (e desmarca o resto).</summary>
    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var item in Groups.SelectMany(g => g.Items))
        {
            item.IsToApply = item.IsRecommended && !item.IsProtected;
        }

        RecalculateCounters();
    }

    /// <summary>Desmarca todos os itens.</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Groups.SelectMany(g => g.Items))
        {
            item.IsToApply = false;
        }

        RecalculateCounters();
    }

    /// <summary>Marca todos os itens reversíveis.</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Groups.SelectMany(g => g.Items))
        {
            item.IsToApply = item.IsReversible || item.IsRecommended;
        }

        RecalculateCounters();
    }

    /// <summary>Reinicia o aplicativo como administrador.</summary>
    [RelayCommand]
    private async Task ElevateAsync()
    {
        if (IsElevated)
        {
            return;
        }

        var restarted = await _elevation.RestartElevatedAsync().ConfigureAwait(true);

        if (restarted)
        {
            System.Windows.Application.Current?.Shutdown();
        }
        else
        {
            await _dialogs.ShowWarningAsync(L("Dlg_ElevationTitle"), L("Msg_NeedAdmin")).ConfigureAwait(true);
        }
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    /// <summary>Aplica um conjunto de itens marcando o estado desejado no modelo.</summary>
    private async Task ApplyAsync(IReadOnlyList<PrivacyItemViewModel> selected, string operationName)
    {
        await RunBusyAsync(async () =>
        {
            // O serviço interpreta IsProtected como o estado DESEJADO.
            var models = selected.Select(i =>
            {
                i.Model.IsProtected = true;

                return i.Model;
            }).ToList();

            var result = await _privacy.ApplyAsync(models).ConfigureAwait(true);

            await ReportResultAsync(result, operationName).ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Exibe o resultado, registra no histórico e recarrega o estado real.</summary>
    private async Task ReportResultAsync(PrivacyApplyResult result, string operationName)
    {
        RequiresElevation = result.RequiresElevation;

        Errors.Clear();

        foreach (var error in result.Errors.Take(12))
        {
            Errors.Add(error);
        }

        OnPropertyChanged(nameof(HasErrors));

        await _history.RecordAsync(
            ActionKind.Privacy,
            operationName,
            result.Summary,
            success: result.IsSuccess).ConfigureAwait(true);

        StatusMessage = result.IsSuccess
            ? LF("Privacy_AppliedSummary", result.AppliedCount, result.SkippedCount, result.FailedCount)
            : LF("Privacy_AppliedWithErrors", result.FailedCount);

        if (result.RequiresElevation)
        {
            StatusMessage += $" · {L("Msg_NeedAdmin")}";
        }

        // Recarrega para refletir o estado real do Windows (itens ignorados voltam expostos).
        var items = await _privacy.GetItemsAsync().ConfigureAwait(true);

        OnUiThread(() => ApplyItems(items));
    }

    private void ApplyItems(IReadOnlyList<PrivacyItem> items)
    {
        Groups.Clear();

        var grouped = _privacy.GroupByCategory(items);

        foreach (var (category, categoryItems) in grouped.OrderBy(g => g.Key))
        {
            var group = new PrivacyGroupViewModel(category, Localization);

            foreach (var item in categoryItems)
            {
                var viewModel = new PrivacyItemViewModel(item, Localization);
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PrivacyItemViewModel.IsToApply))
                    {
                        RecalculateCounters();
                    }
                };

                group.Items.Add(viewModel);
            }

            if (group.Items.Count > 0)
            {
                Groups.Add(group);
            }
        }

        ProtectionScore = _privacy.CalculateProtectionScore(items);

        RecalculateCounters();
    }

    private void RecalculateCounters()
    {
        var all = Groups.SelectMany(g => g.Items).ToList();

        TotalCount = all.Count;
        ProtectedCount = all.Count(i => i.IsProtected);
        ExposedCount = TotalCount - ProtectedCount;
        SelectedCount = all.Count(i => i.IsToApply);

        foreach (var group in Groups)
        {
            group.RefreshCounters();
        }
    }
}

/// <summary>Grupo de itens de privacidade por categoria.</summary>
public sealed partial class PrivacyGroupViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    /// <summary>Cria o grupo.</summary>
    /// <param name="category">Categoria.</param>
    /// <param name="localization">Localização.</param>
    public PrivacyGroupViewModel(PrivacyCategory category, ILocalizationService localization)
    {
        Category = category;
        _localization = localization;
    }

    /// <summary>Categoria do grupo.</summary>
    public PrivacyCategory Category { get; }

    /// <summary>Título localizado da categoria.</summary>
    public string Title => _localization.GetEnumText(Category);

    /// <summary>Ícone (Segoe UI Symbol) da categoria.</summary>
    public string Glyph => Category switch
    {
        PrivacyCategory.Telemetry => "\u223F",
        PrivacyCategory.Tracking => "\u26E8",
        PrivacyCategory.BackgroundApps => "\u2697",
        _ => "\u26E8"
    };

    /// <summary>Itens do grupo.</summary>
    public ObservableCollection<PrivacyItemViewModel> Items { get; } = [];

    /// <summary>Quantidade de itens do grupo.</summary>
    public int Count => Items.Count;

    /// <summary>Itens protegidos do grupo.</summary>
    public int ProtectedCount => Items.Count(i => i.IsProtected);

    /// <summary>Texto de resumo do grupo (ex.: "7/12").</summary>
    public string SummaryText => $"{ProtectedCount}/{Count}";

    /// <summary>Notifica a UI sobre as contagens derivadas do grupo.</summary>
    /// <remarks>
    /// <see cref="Count"/>, <see cref="ProtectedCount"/> e <see cref="SummaryText"/> são
    /// calculados a partir de <see cref="Items"/>; como itens individuais mudam de estado
    /// sem alterar a coleção, a tela chama este método para reemitir as notificações.
    /// </remarks>
    public void RefreshCounters()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(ProtectedCount));
        OnPropertyChanged(nameof(SummaryText));
    }
}

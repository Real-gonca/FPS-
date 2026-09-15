using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Presentation.ViewModels.Items;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// Tela "Análise do Sistema": executa o scan por categorias, lista os problemas
/// encontrados com severidade e permite corrigir o que foi selecionado.
/// </summary>
/// <remarks>
/// <para>
/// <b>Filtro na View, não na fonte.</b> A coleção <see cref="Issues"/> permanece
/// intacta; categoria e busca são aplicados via <see cref="ICollectionView.Filter"/>,
/// o que preserva a seleção do usuário ao trocar o filtro (e evita recarregar dados).
/// </para>
/// <para>
/// <b>Progresso real.</b> O scan roda dentro do <c>ProgressDialog</c>, que recebe o
/// <see cref="IProgress{T}"/> de <see cref="ScanProgress"/> — o usuário vê categoria,
/// passo e percentual, e pode cancelar (o token chega até as regras de análise).
/// </para>
/// </remarks>
public sealed partial class AnalysisViewModel : ViewModelBase
{
    private readonly ISystemAnalyzer _analyzer;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ICollectionView _issuesView;

    private ScanReport? _report;

    /// <summary>Cria o ViewModel da análise.</summary>
    /// <param name="analyzer">Orquestrador das regras de análise.</param>
    /// <param name="dialogs">Diálogos (progresso, confirmação, resultado).</param>
    /// <param name="navigation">Navegação entre telas.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public AnalysisViewModel(
        ISystemAnalyzer analyzer,
        IDialogService dialogs,
        INavigationService navigation,
        ILocalizationService localization,
        ILogger<AnalysisViewModel> logger)
        : base(localization, logger)
    {
        _analyzer = analyzer;
        _dialogs = dialogs;
        _navigation = navigation;

        _issuesView = CollectionViewSource.GetDefaultView(Issues);
        _issuesView.Filter = item => MatchesFilter((AnalysisIssueViewModel)item);

        Categories.Add(new CategoryFilterOption(null, localization["Common_All"]));

        foreach (IssueCategory category in Enum.GetValues<IssueCategory>())
        {
            Categories.Add(new CategoryFilterOption(category, localization.GetEnumText(category)));
        }

        SelectedCategoryFilter = Categories[0];
    }

    /// <summary>Problemas encontrados (fonte do filtro).</summary>
    public ObservableCollection<AnalysisIssueViewModel> Issues { get; } = [];

    /// <summary>Visão filtrada exibida na lista.</summary>
    public ICollectionView IssuesView => _issuesView;

    /// <summary>Opções de filtro de categoria (a primeira é "Todas").</summary>
    public ObservableCollection<CategoryFilterOption> Categories { get; } = [];

    /// <summary>Opção de filtro selecionada.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixSelectedCommand))]
    private CategoryFilterOption? _selectedCategoryFilter;

    /// <summary>Categoria efetiva do filtro (<c>null</c> = todas).</summary>
    public IssueCategory? SelectedCategory => SelectedCategoryFilter?.Value;

    /// <summary>Texto da busca.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Indica se um scan está em andamento.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(FixSelectedCommand))]
    private bool _isScanning;

    /// <summary>Percentual do scan (para a barra embutida).</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>Passo atual do scan.</summary>
    [ObservableProperty]
    private string _currentStepText = string.Empty;

    /// <summary>Total de problemas encontrados.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIssues))]
    private int _issuesFound;

    /// <summary>Total de problemas críticos/altos.</summary>
    [ObservableProperty]
    private int _criticalCount;

    /// <summary>Espaço total recuperável.</summary>
    [ObservableProperty]
    private string _recoverableText = "0 MB";

    /// <summary>Ganho de boot estimado.</summary>
    [ObservableProperty]
    private string _bootImpactText = "—";

    /// <summary>Duração do último scan.</summary>
    [ObservableProperty]
    private string _durationText = "—";

    /// <summary>Categorias que falharam durante o scan.</summary>
    [ObservableProperty]
    private string _failedCategoriesText = string.Empty;

    /// <summary>Indica se há categorias com falha.</summary>
    [ObservableProperty]
    private bool _hasFailedCategories;

    /// <summary>Indica se há problemas listados.</summary>
    public bool HasIssues => IssuesFound > 0;

    /// <summary>Quantidade de itens selecionados para correção.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FixSelectedCommand))]
    private int _selectedCount;

    /// <summary>Executa a análise completa do sistema.</summary>
    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        IsScanning = true;
        ProgressPercent = 0;
        CurrentStepText = L("Common_Loading");

        ScanReport? report = null;

        var completed = await _dialogs.ShowProgressAsync(
            L("Analysis_StartScan"),
            async (progress, cancellationToken) =>
            {
                // Encaminha o progresso para a barra embutida desta tela também.
                var forwarding = new Progress<ScanProgress>(p =>
                {
                    ProgressPercent = p.Percent;
                    CurrentStepText = $"{Localization.GetEnumText(p.CurrentCategory)} · {p.CurrentStepName}";
                    progress.Report(p);
                });

                report = await _analyzer.AnalyzeAsync(forwarding, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        IsScanning = false;

        if (!completed || report is null)
        {
            ProgressPercent = 0;
            CurrentStepText = string.Empty;
            StatusMessage = L("Dlg_CancelledTitle");
            return;
        }

        _report = report;

        ApplyReport(report);

        StatusMessage = LF("Analysis_ScanCompleted", IssuesFound, RecoverableText);
    }

    /// <summary>Analisa apenas as categorias selecionadas (scan rápido).</summary>
    [RelayCommand]
    private async Task ScanCategoriesAsync()
    {
        var categories = new List<IssueCategory>
        {
            IssueCategory.TemporaryFiles,
            IssueCategory.Registry,
            IssueCategory.StartupPrograms,
            IssueCategory.Performance
        };

        IsScanning = true;

        ScanReport? report = null;

        var completed = await _dialogs.ShowProgressAsync(
            L("Common_Scan"),
            async (progress, cancellationToken) =>
            {
                report = await _analyzer.AnalyzeAsync(categories, progress, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        IsScanning = false;

        if (completed && report is not null)
        {
            _report = report;
            ApplyReport(report);
        }
    }

    /// <summary>Corrige os problemas marcados.</summary>
    [RelayCommand(CanExecute = nameof(CanFixSelected))]
    private async Task FixSelectedAsync()
    {
        var selected = Issues.Where(i => i.IsSelected && i.CanAutoFix).Select(i => i.Model).ToList();

        if (selected.Count == 0)
        {
            await _dialogs.ShowInfoAsync(L("Analysis_Fix"), L("Analysis_NoIssues")).ConfigureAwait(true);
            return;
        }

        OptimizationResult? result = null;

        var completed = await _dialogs.ShowProgressAsync(
            L("Analysis_OptimizeSelected"),
            async (progress, cancellationToken) =>
            {
                result = await _analyzer.FixAsync(selected, progress, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!completed || result is null)
        {
            StatusMessage = L("Dlg_CancelledTitle");
            return;
        }

        await _dialogs.ShowOptimizationResultAsync(result).ConfigureAwait(true);

        // Re-analisa para refletir o que foi efetivamente resolvido.
        await StartScanAsync().ConfigureAwait(true);
    }

    /// <summary>Marca todos os itens visíveis.</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var issue in _issuesView.Cast<AnalysisIssueViewModel>())
        {
            if (issue.CanAutoFix)
            {
                issue.IsSelected = true;
            }
        }

        UpdateSelectedCount();
    }

    /// <summary>Desmarca todos os itens.</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var issue in Issues)
        {
            issue.IsSelected = false;
        }

        UpdateSelectedCount();
    }

    /// <summary>Exporta o relatório em JSON.</summary>
    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (_report is null)
        {
            await _dialogs.ShowInfoAsync(L("Common_Export"), L("Analysis_NoIssues")).ConfigureAwait(true);
            return;
        }

        var path = await _dialogs.ShowSaveFileDialogAsync(new FileDialogOptions(
            L("Common_Export"),
            "JSON (*.json)|*.json",
            InitialFileName: $"hl-analise-{DateTime.Now:yyyy-MM-dd-HHmm}.json",
            DefaultExtension: ".json")).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var payload = new
            {
                exportadoEm = DateTime.Now,
                produto = L("App_Title"),
                duracao = _report.Duration.ToString(),
                espacoRecuperavel = _report.TotalRecoverableFormatted,
                categoriasComFalha = _report.FailedCategories,
                problemas = _report.Issues.Select(i => new
                {
                    i.Id,
                    i.Title,
                    i.Description,
                    Categoria = Localization.GetEnumText(i.Category),
                    Severidade = Localization.GetEnumText(i.Severity),
                    i.RecoverableBytes,
                    i.ItemCount,
                    i.CanAutoFix,
                    i.RequiresAdmin
                })
            };

            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);

            await File.WriteAllTextAsync(path, json).ConfigureAwait(true);

            StatusMessage = LF("Common_Exported", path);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Abre a tela de otimização com os itens marcados.</summary>
    [RelayCommand]
    private void GoToOptimization() => _navigation.NavigateTo(NavigationKeys.Optimization);

    /// <summary>Atualiza a contagem de selecionados quando um item muda.</summary>
    private void UpdateSelectedCount() => SelectedCount = Issues.Count(i => i.IsSelected);

    private bool CanStartScan() => !IsScanning;

    private bool CanFixSelected() => !IsScanning && SelectedCount > 0;

    partial void OnSelectedCategoryFilterChanged(CategoryFilterOption? value) => _issuesView.Refresh();

    partial void OnSearchTextChanged(string value) => _issuesView.Refresh();

    private bool MatchesFilter(AnalysisIssueViewModel issue)
    {
        if (SelectedCategoryFilter?.Value is { } category && issue.Category != category)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return issue.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               issue.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               issue.CategoryText.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Aplica um relatório à tela.</summary>
    private void ApplyReport(ScanReport report)
    {
        Issues.Clear();

        foreach (var issue in report.Issues.OrderByDescending(i => i.Severity).ThenByDescending(i => i.RecoverableBytes))
        {
            var item = new AnalysisIssueViewModel(issue, Localization);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AnalysisIssueViewModel.IsSelected))
                {
                    UpdateSelectedCount();
                }
            };

            Issues.Add(item);
        }

        IssuesFound = report.Issues.Count;
        CriticalCount = report.CriticalIssues.Count;
        RecoverableText = report.TotalRecoverableFormatted;
        BootImpactText = report.TotalBootImpactSeconds > 0 ? $"{report.TotalBootImpactSeconds:F0}s" : "—";
        DurationText = report.Duration.TotalSeconds >= 1 ? $"{report.Duration.TotalSeconds:F1}s" : "—";
        ProgressPercent = 100;
        CurrentStepText = string.Empty;

        HasFailedCategories = report.FailedCategories.Count > 0;
        FailedCategoriesText = HasFailedCategories
            ? LF("Analysis_FailedCategories", string.Join(", ", report.FailedCategories))
            : string.Empty;

        UpdateSelectedCount();

        OnPropertyChanged(nameof(HasIssues));
    }
}


/// <summary>Opção do combo de filtro de categorias.</summary>
/// <param name="Value">Categoria (nula para "Todas").</param>
/// <param name="Label">Rótulo localizado.</param>
public sealed record CategoryFilterOption(IssueCategory? Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using HLOptimizerPro.Core.Services.Tweaks;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>Grupo de recomendações por prioridade (Alta / Média / Baixa).</summary>
public sealed partial class RecommendationGroup : ObservableObject
{
    public RecommendationGroup(RecommendationPriority priority, string name, string description, string iconKind)
    {
        Priority = priority;
        Name = name;
        Description = description;
        IconKind = iconKind;
    }

    public RecommendationPriority Priority { get; }
    public string Name { get; }
    public string Description { get; }
    public string IconKind { get; }

    public ObservableCollection<Recommendation> Items { get; } = new();

    [ObservableProperty] private bool _isExpanded = true;

    public int Count => Items.Count;
    public int PendingCount => Items.Count(i => !i.IsApplied);

    public string Summary => Count == 0
        ? "Nada a assinalar neste grupo."
        : PendingCount == 0
            ? $"{Count} recomendação(ões) — todas aplicadas"
            : $"{PendingCount} de {Count} por aplicar";
}

/// <summary>
/// ViewModel das Recomendações (3.7).
///
/// Fonte das recomendações: o último relatório de avaliação (medido, com cache de 30 minutos).
/// Se não existir, é feita uma avaliação completa ao entrar na página. As recomendações são
/// agrupadas por prioridade Alta / Média / Baixa e cada uma tem o botão «Aplicar», que resolve as
/// ações concretas no catálogo e as aplica através do motor de otimizações (com backup e reversão).
/// </summary>
public sealed partial class RecommendationsViewModel : ViewModelBase
{
    private readonly SystemAssessmentService _assessment;
    private readonly RecommendationService _recommendations;
    private readonly OptimizationEngine _engine;
    private readonly OptimizationCatalog _catalog;
    private readonly AssessmentCache _cache;
    private readonly IDialogService _dialogs;

    public RecommendationsViewModel(
        NotificationService notifications,
        SystemAssessmentService assessment,
        RecommendationService recommendations,
        OptimizationEngine engine,
        OptimizationCatalog catalog,
        AssessmentCache cache,
        IDialogService dialogs)
        : base(notifications)
    {
        _assessment = assessment;
        _recommendations = recommendations;
        _engine = engine;
        _catalog = catalog;
        _cache = cache;
        _dialogs = dialogs;

        Title = "Recomendações";
        Subtitle = "O que vale a pena mudar neste computador, por ordem de impacto";
        IconKind = "LightbulbOn";

        Groups.Add(new RecommendationGroup(RecommendationPriority.Alta, "Prioridade alta",
            "Problemas que afetam desempenho ou estabilidade agora.", "AlertCircle"));
        Groups.Add(new RecommendationGroup(RecommendationPriority.Media, "Prioridade média",
            "Melhorias relevantes sem urgência.", "AlertOutline"));
        Groups.Add(new RecommendationGroup(RecommendationPriority.Baixa, "Prioridade baixa",
            "Ajustes de conforto e polimento.", "InformationOutline"));

        RefreshCommandInternal();
    }

    // ---- Estado ---------------------------------------------------------------

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _sourceText = "A recolher dados…";
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool _onlyPending = true;
    [ObservableProperty] private string _lastRunText = "Ainda não aplicou recomendações nesta sessão.";

    public ObservableCollection<RecommendationGroup> Groups { get; } = new();

    public int TotalCount => Groups.Sum(g => g.Count);
    public int PendingCount => Groups.Sum(g => g.PendingCount);

    // ---- Carregamento ----------------------------------------------------------

    /// <summary>Reavalia o sistema (ou reutiliza o relatório recente) e reconstrói as recomendações.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (!_cache.IsValid)
            {
                ProgressText = "A avaliar o sistema…";
                var progress = new Progress<string>(m => ProgressText = m);
                var report = await _assessment.AssessAsync(progress);
                _cache.Store(report);
            }

            var report2 = _cache.Last!;
            var list = _recommendations.Build(report2);

            Groups[0].Items.Clear();
            Groups[1].Items.Clear();
            Groups[2].Items.Clear();

            foreach (var recommendation in list)
            {
                var group = Groups.FirstOrDefault(g => g.Priority == recommendation.Priority) ?? Groups[1];
                group.Items.Add(recommendation);
            }

            HasData = list.Count > 0;
            SourceText = _cache.DescribeAge();

            SummaryText = list.Count == 0
                ? "Não há nada a recomendar — o sistema está bem configurado."
                : $"{list.Count} recomendações • {list.Count(r => r.Priority == RecommendationPriority.Alta)} de prioridade alta • " +
                  $"Pontuação atual {report2.OverallScoreText} ({report2.Grade})";

            foreach (var group in Groups) OnPropertyChanged(group.GetType().Name);

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(PendingCount));
            ProgressText = string.Empty;
        }, "A preparar as recomendações…");
    }

    private void RefreshCommandInternal()
    {
        // Não corremos a avaliação no construtor (evita trabalho no arranque da aplicação);
        // a página carrega os dados quando é aberta.
        SourceText = "Abra a página para avaliar o sistema.";
    }

    // ---- Aplicar ---------------------------------------------------------------

    /// <summary>Aplica uma recomendação individual (resolve as ações e passa pelo motor).</summary>
    [RelayCommand]
    private async Task ApplyAsync(Recommendation? recommendation)
    {
        if (recommendation is null) return;

        var actions = _recommendations.ResolveActions(recommendation);

        if (actions.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Sem ações",
                "Esta recomendação não tem uma ação automática associada — siga as instruções da descrição.");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            recommendation.Title,
            $"{recommendation.Description}\n\nBenefício esperado: {recommendation.ExpectedBenefit}\n\n" +
            $"Ações que vão ser aplicadas:\n{string.Join("\n", actions.Select(a => $"• {a.Title} — {a.Risk}"))}",
            "Aplicar");

        if (!confirmed) return;

        var simulation = _catalog.SimulationMode;

        await RunBusyAsync(async () =>
        {
            var result = await _engine.ApplyAsync(actions, new OptimizationRunOptions
            {
                BatchName = recommendation.Title,
                Simulation = simulation,
                CreateRestorePoint = true,
                ActivityCategory = "Recomendações",
            });

            if (simulation)
                await _dialogs.ShowSimulationAsync("Modo Simular — recomendação", _engine.Preview(actions));

            Report(result, "Recomendações");

            if (result.Success && !simulation)
            {
                recommendation.IsApplied = true;
                LastRunText = $"{DateTime.Now:HH:mm:ss} — {recommendation.Title}: {result.Message}";
            }

            RefreshGroups();
        }, "A aplicar a recomendação…");
    }

    /// <summary>Aplica todas as recomendações pendentes de um grupo (ex.: todas as de prioridade alta).</summary>
    [RelayCommand]
    private async Task ApplyGroupAsync(RecommendationGroup? group)
    {
        if (group is null) return;

        var pending = group.Items.Where(r => !r.IsApplied).ToList();
        if (pending.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Nada a aplicar",
                "Todas as recomendações deste grupo já foram aplicadas.");
            return;
        }

        var actions = pending.SelectMany(_recommendations.ResolveActions).DistinctBy(a => a.Key).ToList();

        if (actions.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Sem ações automáticas",
                "As recomendações deste grupo não têm ações automáticas associadas.");
            return;
        }

        var risky = actions.Count(a => a.RequiresConfirmation || a.RiskLevel != HealthLevel.Otimo);

        var confirmed = await _dialogs.ConfirmAsync(
            $"Aplicar {group.Name}",
            $"{actions.Count} otimizações vão ser aplicadas ({pending.Count} recomendações):\n\n" +
            string.Join("\n", actions.Take(15).Select(a => $"• {a.Title} — {a.Risk}")) +
            (actions.Count > 15 ? $"\n… e mais {actions.Count - 15}." : string.Empty) +
            "\n\nPonto de restauro e backup dos valores originais incluídos.",
            "Aplicar tudo",
            danger: risky > 0);

        if (!confirmed) return;

        var simulation = _catalog.SimulationMode;

        await RunBusyAsync(async () =>
        {
            var result = await _engine.ApplyAsync(actions, new OptimizationRunOptions
            {
                BatchName = group.Name,
                Simulation = simulation,
                CreateRestorePoint = true,
                ActivityCategory = "Recomendações",
            });

            if (simulation)
                await _dialogs.ShowSimulationAsync("Modo Simular — grupo", _engine.Preview(actions));

            Report(result, "Recomendações");

            if (result.Success && !simulation)
                foreach (var recommendation in pending) recommendation.IsApplied = true;

            LastRunText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
            RefreshGroups();
        }, $"A aplicar {group.Name}…");
    }

    /// <summary>Mostra/oculta as recomendações de um grupo.</summary>
    [RelayCommand]
    private void ToggleGroup(RecommendationGroup? group)
    {
        if (group is null) return;
        group.IsExpanded = !group.IsExpanded;
    }

    private void RefreshGroups()
    {
        _catalog.Refresh();

        foreach (var group in Groups)
            OnPropertyChanged(group.GetType().Name);

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PendingCount));
    }

    partial void OnOnlyPendingChanged(bool value) => OnPropertyChanged(nameof(Groups));

    public override async Task OnNavigatedToAsync(object? parameter = null)
    {
        await base.OnNavigatedToAsync(parameter);

        if (!HasData || !_cache.IsValid)
            await LoadAsync();
    }
}

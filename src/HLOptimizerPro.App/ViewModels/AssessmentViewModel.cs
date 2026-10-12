using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>
/// ViewModel da Avaliação do Sistema (3.3).
///
/// Tudo o que aparece aqui vem de medições reais feitas pelo <see cref="SystemAssessmentService"/>:
/// arranque (chaves Run + tarefas), espaço em disco, temperaturas, antivírus/firewall, atualizações,
/// fragmentação, serviços, energia e privacidade. A pontuação é calculada pelo
/// <see cref="ScoringService"/> com pesos fixos e auditáveis (0,30 / 0,25 / 0,25 / 0,20) e cada
/// critério mostra o valor medido e quantos pontos retirou — o utilizador consegue perceber o número.
/// </summary>
public sealed partial class AssessmentViewModel : ViewModelBase
{
    private readonly SystemAssessmentService _assessment;
    private readonly RecommendationService _recommendations;
    private readonly ReportService _reportService;
    private readonly ShellStateService _shell;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;

    private AssessmentReport? _report;

    public AssessmentViewModel(
        NotificationService notifications,
        SystemAssessmentService assessment,
        RecommendationService recommendations,
        ReportService reportService,
        ShellStateService shell,
        IDialogService dialogs,
        INavigationService navigation)
        : base(notifications)
    {
        _assessment = assessment;
        _recommendations = recommendations;
        _reportService = reportService;
        _shell = shell;
        _dialogs = dialogs;
        _navigation = navigation;

        Title = "Avaliação";
        Subtitle = "Pontuação real do seu computador, medida agora";
        IconKind = "ChartBox";
    }

    // ---- Estado ---------------------------------------------------------------

    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private double _overallScore;
    [ObservableProperty] private string _overallScoreText = "—";
    [ObservableProperty] private string _gradeText = "Sem avaliação";
    [ObservableProperty] private HealthLevel _gradeHealth = HealthLevel.Desconhecido;
    [ObservableProperty] private string _diagnosis = string.Empty;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _generatedAtText = string.Empty;
    [ObservableProperty] private string _summaryLine = string.Empty;

    /// <summary>Sub-pontuações (Desempenho, Estabilidade, Segurança, Otimização).</summary>
    public ObservableCollection<ScoreBreakdown> Breakdowns { get; } = new();

    /// <summary>Recomendações geradas pela avaliação (atalho para a página Recomendações).</summary>
    public ObservableCollection<Recommendation> Recommendations { get; } = new();

    /// <summary>Sugestão de pontuação máxima: 0 problemas = 100 %.</summary>
    public string ScoreHint => HasReport
        ? $"Pontuação máxima possível: 100 %. Peso das áreas: " +
          string.Join(" • ", Breakdowns.Select(b => $"{b.Name} {b.Weight:P0}"))
        : "Execute a avaliação para obter a pontuação do seu sistema.";

    // ---- Avaliação ------------------------------------------------------------

    [RelayCommand]
    private async Task RunAssessmentAsync()
    {
        await RunBusyAsync(async () =>
        {
            ProgressText = "A iniciar avaliação…";

            var progress = new Progress<string>(message => ProgressText = message);
            var report = await _assessment.AssessAsync(progress);

            _report = report;

            OverallScore = report.OverallScore;
            OverallScoreText = report.OverallScoreText;
            GradeText = report.Grade;
            GradeHealth = report.OverallScore switch
            {
                >= 85 => HealthLevel.Otimo,
                >= 65 => HealthLevel.Atencao,
                _ => HealthLevel.Critico,
            };
            Diagnosis = report.Diagnosis;
            GeneratedAtText = $"Avaliação de {report.GeneratedAt:dd/MM/yyyy HH:mm}";
            SummaryLine = BuildSummaryLine(report);

            Breakdowns.Clear();
            foreach (var breakdown in report.Breakdowns) Breakdowns.Add(breakdown);

            var recommendations = _recommendations.Build(report);
            Recommendations.Clear();
            foreach (var recommendation in recommendations) Recommendations.Add(recommendation);

            HasReport = true;
            _shell.UpdateSystemState(GradeHealth);
            _shell.PendingOptimizations = recommendations.Count(r => !r.IsApplied);

            SetStatus($"Avaliação concluída: {report.OverallScoreText} — {report.Grade}.");
            Notifications.Success("Avaliação concluída",
                $"Pontuação {report.OverallScoreText} ({report.Grade}). {recommendations.Count} recomendação(ões) geradas.");
        }, "A avaliar o sistema…");
    }

    private static string BuildSummaryLine(AssessmentReport report)
    {
        var problems = report.Breakdowns
            .SelectMany(b => b.Criteria)
            .Where(c => c.IsProblem)
            .ToList();

        return problems.Count == 0
            ? "Não foram encontrados problemas relevantes nas verificações efetuadas."
            : $"{problems.Count} ponto(s) a melhorar: " +
              string.Join(", ", problems.Take(4).Select(p => p.Name.ToLowerInvariant()));
    }

    // ---- Exportação -----------------------------------------------------------

    /// <summary>"Ver Relatório Completo": exporta em PDF ou HTML e abre o ficheiro.</summary>
    [RelayCommand]
    private async Task ExportReportAsync(string? format)
    {
        if (_report is null)
        {
            await _dialogs.ShowInfoAsync("Sem avaliação", "Execute primeiro a avaliação do sistema.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var isPdf = string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase);

            BusyMessage = isPdf ? "A gerar o relatório em PDF…" : "A gerar o relatório em HTML…";

            var result = await Task.Run(() => isPdf
                ? _reportService.ExportPdf(_report)
                : _reportService.ExportHtml(_report));

            if (!result.Success)
            {
                Report(result, "Relatório");
                return;
            }

            SetStatus(result.Message);
            Notifications.Success("Relatório gerado", result.Message);

            var open = await _dialogs.ConfirmAsync(
                "Relatório pronto",
                $"{result.Value}\n\nQuer abrir o relatório agora?",
                "Abrir relatório");

            if (open) _dialogs.OpenInShell(result.Value!);
        }, "A exportar o relatório…");
    }

    [RelayCommand]
    private void OpenReportsFolder()
        => _dialogs.OpenInShell(AppPaths.Reports);

    /// <summary>Envia o utilizador para as otimizações sugeridas pela avaliação.</summary>
    [RelayCommand]
    private async Task GoToRecommendationsAsync()
        => await _navigation.NavigateToAsync(NavigationKeys.Recommendations);
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Application.Analysis.Rules;

/// <summary>Avalia a exposição de privacidade (telemetria, rastreamento, permissões).</summary>
public sealed class PrivacyAnalysisRule : IAnalysisRule
{
    private readonly IPrivacyService _privacyService;

    /// <summary>Cria a regra.</summary>
    /// <param name="privacyService">Serviço de privacidade.</param>
    public PrivacyAnalysisRule(IPrivacyService privacyService)
    {
        _privacyService = privacyService;
    }

    /// <inheritdoc />
    public string Id => "analysis.privacy";

    /// <inheritdoc />
    public string Name => "Verificar privacidade e rastreamento";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Privacy;

    /// <inheritdoc />
    public int Order => 80;

    /// <inheritdoc />
    public bool RequiresAdmin => false;

    /// <inheritdoc />
    public double Weight => 1.0d;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnalysisIssue>> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var items = context.PrivacyItems ?? await _privacyService.GetItemsAsync(cancellationToken).ConfigureAwait(false);
        context.PrivacyItems ??= items;

        if (items.Count == 0)
        {
            return [];
        }

        var exposed = items.Where(i => !i.IsProtected).ToList();
        var protectionScore = _privacyService.CalculateProtectionScore(items);

        if (exposed.Count == 0)
        {
            return [];
        }

        var issues = new List<AnalysisIssue>();

        var severity = protectionScore switch
        {
            < 40 => Severity.High,
            < 70 => Severity.Medium,
            _ => Severity.Low
        };

        issues.Add(new AnalysisIssue(
            Id: "analysis.privacy.exposed",
            Title: "Rastreamento e telemetria ativos",
            Description: $"{exposed.Count} de {items.Count} itens de privacidade continuam expostos (nível de proteção atual: {protectionScore:F0}%). Exemplos: {string.Join(", ", exposed.Take(3).Select(i => i.DisplayName))}.",
            Category: Category,
            Severity: severity,
            RecommendedAction: "Aplicar recomendações",
            CanAutoFix: true,
            RequiresAdmin: exposed.Any(i => i.RequiresAdmin),
            ItemCount: exposed.Count,
            FixIdentifier: Optimization.OptimizationStepIds.Telemetry));

        var recommended = exposed.Where(i => i.IsRecommended).ToList();

        if (recommended.Count > 0 && recommended.Count != exposed.Count)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.privacy.recommended",
                Title: "Recomendações de privacidade pendentes",
                Description: $"{recommended.Count} ajuste(s) recomendado(s) pelo HL PRO OPTIMIZER ainda não foram aplicados e são seguros de desativar.",
                Category: Category,
                Severity: Severity.Low,
                RecommendedAction: "Aplicar recomendações",
                CanAutoFix: true,
                RequiresAdmin: recommended.Any(i => i.RequiresAdmin),
                ItemCount: recommended.Count,
                FixIdentifier: Optimization.OptimizationStepIds.Telemetry));
        }

        return issues;
    }
}

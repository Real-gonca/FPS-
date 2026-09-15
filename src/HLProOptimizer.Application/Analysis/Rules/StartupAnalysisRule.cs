using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Analysis.Rules;

/// <summary>Detecta programas de inicialização pesados e entradas órfãs.</summary>
public sealed class StartupAnalysisRule : IAnalysisRule
{
    private readonly ILogger<StartupAnalysisRule> _logger;

    /// <summary>Cria a regra.</summary>
    /// <param name="logger">Logger.</param>
    public StartupAnalysisRule(ILogger<StartupAnalysisRule> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => "analysis.startup";

    /// <inheritdoc />
    public string Name => "Verificar programas de inicialização";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.StartupPrograms;

    /// <inheritdoc />
    public int Order => 50;

    /// <inheritdoc />
    public bool RequiresAdmin => false;

    /// <inheritdoc />
    public double Weight => 1.0d;

    /// <inheritdoc />
    public Task<IReadOnlyList<AnalysisIssue>> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var programs = context.StartupPrograms ?? [];

        if (programs.Count == 0)
        {
            _logger.LogDebug("Nenhum programa de inicialização para analisar.");
            return Task.FromResult<IReadOnlyList<AnalysisIssue>>([]);
        }

        var issues = new List<AnalysisIssue>();

        var orphaned = programs.Where(p => !p.FileExists).ToList();

        if (orphaned.Count > 0)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.startup.orphaned",
                Title: "Entradas de inicialização órfãs",
                Description: $"{orphaned.Count} programa(s) configurados para iniciar apontam para arquivos que não existem mais: {string.Join(", ", orphaned.Take(3).Select(p => p.Name))}.",
                Category: Category,
                Severity: Severity.Medium,
                RecommendedAction: "Remover entradas",
                CanAutoFix: true,
                ItemCount: orphaned.Count,
                FixIdentifier: Optimization.OptimizationStepIds.Startup));
        }

        var highImpact = programs
            .Where(p => p.IsEnabled && p.Impact == StartupImpact.High)
            .OrderByDescending(p => p.EstimatedSeconds)
            .ToList();

        if (highImpact.Count > 0)
        {
            var seconds = highImpact.Sum(p => p.EstimatedSeconds);

            issues.Add(new AnalysisIssue(
                Id: "analysis.startup.highimpact",
                Title: "Programas de inicialização de alto impacto",
                Description: $"{highImpact.Count} programa(s) pesados iniciam com o Windows, somando ~{seconds:F1}s ao boot: {string.Join(", ", highImpact.Take(4).Select(p => p.Name))}.",
                Category: Category,
                Severity: highImpact.Count >= 3 ? Severity.High : Severity.Medium,
                EstimatedBootImpactSeconds: seconds,
                RecommendedAction: "Desativar não essenciais",
                CanAutoFix: true,
                ItemCount: highImpact.Count,
                FixIdentifier: Optimization.OptimizationStepIds.Startup));
        }

        var enabledCount = programs.Count(p => p.IsEnabled);

        if (enabledCount > 15)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.startup.toomany",
                Title: "Muitos programas na inicialização",
                Description: $"{enabledCount} programas iniciam automaticamente com o Windows, consumindo CPU, disco e RAM durante o boot.",
                Category: Category,
                Severity: Severity.Low,
                EstimatedBootImpactSeconds: Math.Min(10d, enabledCount * 0.25d),
                RecommendedAction: "Revisar inicialização",
                CanAutoFix: false,
                ItemCount: enabledCount,
                FixIdentifier: Optimization.OptimizationStepIds.Startup));
        }

        return Task.FromResult<IReadOnlyList<AnalysisIssue>>(issues);
    }
}

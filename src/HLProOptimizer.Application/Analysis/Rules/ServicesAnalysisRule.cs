using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Application.Optimization;

namespace HLProOptimizer.Application.Analysis.Rules;

/// <summary>
/// Detecta serviços não essenciais configurados como automáticos (custo de boot e
/// de memória) e serviços críticos parados (risco de estabilidade).
/// </summary>
public sealed class ServicesAnalysisRule : IAnalysisRule
{
    /// <inheritdoc />
    public string Id => "analysis.services";

    /// <inheritdoc />
    public string Name => "Verificar serviços desnecessários";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Services;

    /// <inheritdoc />
    public int Order => 60;

    /// <inheritdoc />
    public bool RequiresAdmin => true;

    /// <inheritdoc />
    public double Weight => 1.0d;

    /// <inheritdoc />
    public Task<IReadOnlyList<AnalysisIssue>> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = context.Services ?? [];

        if (services.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<AnalysisIssue>>([]);
        }

        var issues = new List<AnalysisIssue>();

        // 1) Serviços do catálogo que ainda estão automáticos.
        var candidates = ServiceOptimizationCatalog.ForLevel(OptimizationLevel.Aggressive);
        var stillAutomatic = services
            .Where(s => candidates.Any(c => string.Equals(c.ServiceName, s.ServiceName, StringComparison.OrdinalIgnoreCase)))
            .Where(s => s.StartupKind is ServiceStartupKind.Automatic or ServiceStartupKind.AutomaticDelayed)
            .ToList();

        if (stillAutomatic.Count > 0)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.services.nonessential",
                Title: "Serviços não essenciais iniciando automaticamente",
                Description: $"{stillAutomatic.Count} serviço(s) sem impacto no uso diário iniciam junto com o Windows: {string.Join(", ", stillAutomatic.Take(4).Select(s => s.DisplayName))}.",
                Category: Category,
                Severity: stillAutomatic.Count >= 5 ? Severity.Medium : Severity.Low,
                EstimatedBootImpactSeconds: Math.Min(6d, stillAutomatic.Count * 0.4d),
                RecommendedAction: "Otimizar serviços",
                CanAutoFix: true,
                RequiresAdmin: true,
                ItemCount: stillAutomatic.Count,
                FixIdentifier: OptimizationStepIds.Services));
        }

        // 2) Serviços críticos parados.
        var criticalStopped = services
            .Where(s => ServiceOptimizationCatalog.CriticalServices.Contains(s.ServiceName, StringComparer.OrdinalIgnoreCase))
            .Where(s => !s.IsRunning)
            .ToList();

        if (criticalStopped.Count > 0)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.services.criticalstopped",
                Title: "Serviços essenciais parados",
                Description: $"{criticalStopped.Count} serviço(s) essenciais do Windows não estão em execução: {string.Join(", ", criticalStopped.Select(s => s.DisplayName))}. Isso pode causar instabilidade.",
                Category: Category,
                Severity: Severity.Critical,
                RecommendedAction: "Iniciar serviços",
                CanAutoFix: true,
                RequiresAdmin: true,
                ItemCount: criticalStopped.Count));
        }

        // 3) Telemetria ainda ativa.
        var diagTrack = services.FirstOrDefault(s =>
            string.Equals(s.ServiceName, "DiagTrack", StringComparison.OrdinalIgnoreCase));

        if (diagTrack is { IsRunning: true })
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.services.diagtrack",
                Title: "Telemetria do Windows ativa",
                Description: "O serviço 'Connected User Experiences and Telemetry' (DiagTrack) está em execução e envia dados de diagnóstico à Microsoft.",
                Category: Category,
                Severity: Severity.High,
                RecommendedAction: "Desativar telemetria",
                CanAutoFix: true,
                RequiresAdmin: true,
                ItemCount: 1,
                FixIdentifier: OptimizationStepIds.Telemetry));
        }

        return Task.FromResult<IReadOnlyList<AnalysisIssue>>(issues);
    }
}

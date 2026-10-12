using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Tweaks;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Junta as recomendações da avaliação com o estado real do catálogo de otimizações e devolve
/// uma lista por prioridade, pronta para a tela Recomendações (3.7).
///
/// Cada recomendação tem:
///   • título e descrição em português,
///   • prioridade (alta/média/baixa) baseada no impacto medido,
///   • chave de ação que o botão "Aplicar" executa através do motor de otimizações.
/// </summary>
public sealed class RecommendationService
{
    private readonly ILogger<RecommendationService> _logger;
    private readonly OptimizationCatalog _catalog;

    public RecommendationService(ILogger<RecommendationService> logger, OptimizationCatalog catalog)
    {
        _logger = logger;
        _catalog = catalog;
    }

    /// <summary>Recomendações combinadas (avaliação + estado do sistema), ordenadas por prioridade.</summary>
    public IReadOnlyList<Recommendation> Build(AssessmentReport? report)
    {
        var recommendations = new List<Recommendation>();

        if (report is not null)
            recommendations.AddRange(report.Recommendations);

        // Recomendações baseadas no estado do catálogo: otimizações aplicáveis que ainda não estão aplicadas.
        var pending = _catalog.All
            .Where(a => a.IsAvailable && !OptimizationCatalog.IsApplied(a))
            .ToList();

        // Agrupamos por área para não gerar 40 recomendações iguais (a UI mostra o detalhe em Otimizações).
        foreach (var group in pending.GroupBy(a => a.Area))
        {
            var (title, category, icon, key) = DescribeArea(group.Key, group.Count());

            recommendations.Add(new Recommendation
            {
                Id = $"rec-area-{group.Key}",
                Title = title,
                Description = string.Join(" • ", group.Take(4).Select(a => a.Title)) +
                              (group.Count() > 4 ? $" • (+{group.Count() - 4} ajustes)" : string.Empty),
                Priority = group.Count() >= 5 ? RecommendationPriority.Alta : RecommendationPriority.Media,
                Category = category,
                ExpectedBenefit = ExpectedBenefitFor(group.Key),
                ActionKey = key,
                IconKind = icon,
            });
        }

        return recommendations
            .GroupBy(r => r.Id)
            .Select(g => g.First())
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Category)
            .ToList();
    }

    /// <summary>Aplica uma recomendação: executa as ações correspondentes pelo motor.</summary>
    public IReadOnlyList<OptimizationAction> ResolveActions(Recommendation recommendation)
    {
        if (string.IsNullOrWhiteSpace(recommendation.ActionKey))
            return Array.Empty<OptimizationAction>();

        // Recomendações que correspondem a módulos (e não a tweaks do catálogo).
        if (recommendation.ActionKey.StartsWith("rec-area-", StringComparison.OrdinalIgnoreCase))
        {
            var areaName = recommendation.ActionKey["rec-area-".Length..];
            return Enum.TryParse<OptimizationArea>(areaName, out var area)
                ? _catalog.ByArea(area)
                : Array.Empty<OptimizationAction>();
        }

        var action = _catalog.Find(recommendation.ActionKey);
        return action is null ? Array.Empty<OptimizationAction>() : new[] { action };
    }

    private static (string Title, RecommendationCategory Category, string Icon, string? ActionKey) DescribeArea(
        OptimizationArea area, int count) => area switch
    {
        OptimizationArea.DesempenhoSistema =>
            ($"{count} ajustes de desempenho disponíveis", RecommendationCategory.Desempenho, "Speedometer", $"rec-area-{area}"),
        OptimizationArea.Privacidade =>
            ($"{count} ajustes de privacidade disponíveis", RecommendationCategory.Seguranca, "ShieldLock", $"rec-area-{area}"),
        OptimizationArea.UnidadeDisco =>
            ($"{count} ajustes de disco disponíveis", RecommendationCategory.Armazenamento, "HarddiskPlus", $"rec-area-{area}"),
        OptimizationArea.WindowsUpdate =>
            ("Configurar o Windows Update para o seu ritmo", RecommendationCategory.Seguranca, "ShieldSync", $"rec-area-{area}"),
        OptimizationArea.Toque =>
            ($"{count} ajustes de ecrã tátil disponíveis", RecommendationCategory.Sistema, "GestureTap", $"rec-area-{area}"),
        OptimizationArea.Aplicativos =>
            ($"{count} ajustes de aplicações disponíveis", RecommendationCategory.Sistema, "Apps", $"rec-area-{area}"),
        _ => ($"{count} otimizações disponíveis", RecommendationCategory.Desempenho, "TuneVariant", $"rec-area-{area}"),
    };

    private static string ExpectedBenefitFor(OptimizationArea area) => area switch
    {
        OptimizationArea.DesempenhoSistema => "Arranque mais rápido, menos uso de recursos em segundo plano e interface mais fluida.",
        OptimizationArea.Privacidade => "Menos dados recolhidos e enviados, com menos tráfego de rede em segundo plano.",
        OptimizationArea.UnidadeDisco => "Mais espaço livre, tempos de acesso menores e menos desgaste do disco.",
        OptimizationArea.WindowsUpdate => "Atualizações sob controlo, sem reinícios inesperados no meio do trabalho.",
        OptimizationArea.Toque => "Menos interferência de gestos em aplicações em ecrã inteiro.",
        OptimizationArea.Aplicativos => "Menos processos residentes e menos consumo de memória.",
        _ => "Melhoria geral de desempenho e estabilidade.",
    };
}

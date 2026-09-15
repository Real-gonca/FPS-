using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Application.Analysis.Rules;

/// <summary>Avalia a configuração de rede com foco em latência (DNS, adaptadores, Wi-Fi).</summary>
public sealed class NetworkAnalysisRule : IAnalysisRule
{
    private readonly IDnsService _dnsService;

    /// <summary>Cria a regra.</summary>
    /// <param name="dnsService">Serviço de DNS.</param>
    public NetworkAnalysisRule(IDnsService dnsService)
    {
        _dnsService = dnsService;
    }

    /// <inheritdoc />
    public string Id => "analysis.network";

    /// <inheritdoc />
    public string Name => "Verificar configuração de rede";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Network;

    /// <inheritdoc />
    public int Order => 110;

    /// <inheritdoc />
    public bool RequiresAdmin => false;

    /// <inheritdoc />
    public double Weight => 0.8d;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnalysisIssue>> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<AnalysisIssue>();

        var configurations = await _dnsService.GetConfigurationsAsync(cancellationToken).ConfigureAwait(false);

        // DNS automático (fornecido pelo provedor) costuma ser mais lento que um DNS público.
        var automatic = configurations.Where(c => c.IsAutomatic).ToList();

        if (automatic.Count > 0)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.network.dns",
                Title: "DNS do provedor em uso",
                Description: $"{automatic.Count} adaptador(es) usam o DNS fornecido pelo provedor, geralmente mais lento para resolver nomes. Um DNS público (Cloudflare/Google) reduz o tempo de resolução.",
                Category: Category,
                Severity: Severity.Low,
                RecommendedAction: "Configurar DNS",
                CanAutoFix: true,
                RequiresAdmin: true,
                ItemCount: automatic.Count));
        }

        // Adaptador Wi-Fi com link baixo indica sinal fraco (impacto direto em jogos online).
        var weakWiFi = context.Profile.NetworkAdapters
            .FirstOrDefault(a => a.IsPhysical && a.IsOperational &&
                                 a.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) &&
                                 a.LinkSpeedMbps > 0 && a.LinkSpeedMbps < 100);

        if (weakWiFi is not null)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.network.wifi",
                Title: "Conexão Wi-Fi lenta",
                Description: $"O adaptador '{weakWiFi.Name}' está negociado a {weakWiFi.LinkSpeedMbps:F0} Mbps. Para jogos competitivos, prefira cabo Ethernet ou a faixa de 5 GHz.",
                Category: Category,
                Severity: Severity.Medium,
                RecommendedAction: "Melhorar conexão",
                CanAutoFix: false));
        }

        // Sem adaptador operacional: nada de rede.
        if (!context.Profile.NetworkAdapters.Any(a => a.IsOperational))
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.network.noadapter",
                Title: "Nenhuma conexão de rede ativa",
                Description: "Nenhum adaptador de rede está conectado. Verifique o cabo/Wi-Fi ou execute o reparo de rede nas Ferramentas.",
                Category: Category,
                Severity: Severity.High,
                RecommendedAction: "Reparar rede",
                CanAutoFix: false,
                RequiresAdmin: true));
        }

        return issues;
    }
}

using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>
/// Um problema/oportunidade encontrado pela análise do sistema.
/// É a unidade exibida na lista "Resultados da Análise" e nas "Recomendações"
/// do Dashboard.
/// </summary>
/// <param name="Id">Identificador estável do problema (ex.: "temp.user").</param>
/// <param name="Title">Título curto para a UI.</param>
/// <param name="Description">Explicação do impacto.</param>
/// <param name="Category">Categoria da análise.</param>
/// <param name="Severity">Gravidade (define cor/ordenação).</param>
/// <param name="RecoverableBytes">Espaço recuperável (0 quando não aplicável).</param>
/// <param name="EstimatedBootImpactSeconds">Ganho estimado de boot em segundos.</param>
/// <param name="RecommendedAction">Ação sugerida (texto do botão na UI).</param>
/// <param name="CanAutoFix">Se o problema pode ser corrigido automaticamente.</param>
/// <param name="RequiresAdmin">Se a correção exige elevação.</param>
/// <param name="ItemCount">Quantidade de itens afetados (arquivos, chaves, programas).</param>
/// <param name="FixIdentifier">Chave do <c>IOptimizationStep</c> que resolve o problema.</param>
public sealed record AnalysisIssue(
    string Id,
    string Title,
    string Description,
    IssueCategory Category,
    Severity Severity,
    long RecoverableBytes = 0,
    double EstimatedBootImpactSeconds = 0,
    string RecommendedAction = "Corrigir",
    bool CanAutoFix = true,
    bool RequiresAdmin = false,
    int ItemCount = 0,
    string? FixIdentifier = null)
{
    /// <summary>Espaço recuperável formatado (vazio quando não há ganho de disco).</summary>
    public string RecoverableFormatted => RecoverableBytes > 0 ? ByteFormat.Format(RecoverableBytes) : string.Empty;

    /// <summary>Indica se há ganho mensurável de boot.</summary>
    public bool HasBootImpact => EstimatedBootImpactSeconds > 0;
}

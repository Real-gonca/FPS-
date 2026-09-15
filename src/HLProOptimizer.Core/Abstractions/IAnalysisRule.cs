using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Regra individual de análise (padrão Strategy). Cada categoria do scan é uma
/// implementação isolada, o que mantém o analisador extensível (Open/Closed):
/// adicionar uma nova categoria = adicionar uma nova classe registrada no DI.
/// </summary>
public interface IAnalysisRule
{
    /// <summary>Identificador único da regra (ex.: "analysis.tempfiles").</summary>
    string Id { get; }

    /// <summary>Nome legível exibido no progresso do scan.</summary>
    string Name { get; }

    /// <summary>Categoria que a regra analisa.</summary>
    IssueCategory Category { get; }

    /// <summary>Ordem de execução (menor primeiro).</summary>
    int Order { get; }

    /// <summary>Indica se a regra precisa de administrador para produzir resultados completos.</summary>
    bool RequiresAdmin { get; }

    /// <summary>Peso da regra no score de limpeza/desempenho.</summary>
    double Weight { get; }

    /// <summary>Executa a análise.</summary>
    /// <param name="context">Contexto compartilhado (perfil, caches).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Problemas encontrados (lista vazia quando tudo está saudável).</returns>
    Task<IReadOnlyList<AnalysisIssue>> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default);
}

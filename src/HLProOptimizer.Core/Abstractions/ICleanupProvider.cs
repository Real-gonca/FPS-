using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Provedor de alvos de limpeza (Strategy). Há um provedor para arquivos do
/// sistema, um para aplicativos/navegadores e um para o registro - todos
/// compostos pelo serviço de limpeza da camada Application.
/// </summary>
public interface ICleanupProvider
{
    /// <summary>Nome do provedor (para logs).</summary>
    string Name { get; }

    /// <summary>Categoria principal que o provedor atende.</summary>
    IssueCategory Category { get; }

    /// <summary>Ordem de execução.</summary>
    int Order { get; }

    /// <summary>Indica se o provedor requer administrador.</summary>
    bool RequiresAdmin { get; }

    /// <summary>Descobre os alvos de limpeza e seus tamanhos estimados.</summary>
    /// <param name="level">Nível de agressividade (filtra alvos arriscados).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<CleanupTarget>> ScanAsync(OptimizationLevel level, CancellationToken cancellationToken = default);

    /// <summary>Executa a limpeza dos alvos selecionados.</summary>
    /// <param name="targets">Alvos marcados pelo usuário.</param>
    /// <param name="progress">Progresso por alvo.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CleanupResult> CleanAsync(
        IReadOnlyList<CleanupTarget> targets,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

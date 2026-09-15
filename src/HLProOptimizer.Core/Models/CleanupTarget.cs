using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>
/// Um grupo de arquivos/entradas limpáveis (ex.: "Windows Temp", "Cache do Chrome").
/// A tela de Limpeza exibe estes itens em uma TreeView agrupada por categoria.
/// </summary>
public sealed class CleanupTarget
{
    /// <summary>Identificador estável (ex.: "temp.windows").</summary>
    public required string Id { get; init; }

    /// <summary>Nome exibido na UI.</summary>
    public required string Name { get; init; }

    /// <summary>Descrição do que será removido.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Categoria à qual pertence.</summary>
    public IssueCategory Category { get; init; }

    /// <summary>
    /// Nome do <c>ICleanupProvider</c> que descobriu este alvo. Preenchido pelo
    /// <c>ICleanupService</c> para permitir rotear a limpeza de volta ao provedor.
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Caminhos/padrões que compõem este alvo.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>Tamanho estimado em bytes.</summary>
    public long EstimatedBytes { get; init; }

    /// <summary>Quantidade de arquivos/entradas.</summary>
    public int FileCount { get; init; }

    /// <summary>Se a remoção é considerada segura (sem efeito colateral relevante).</summary>
    public bool IsSafe { get; init; } = true;

    /// <summary>Se exige administrador.</summary>
    public bool RequiresAdmin { get; init; }

    /// <summary>Se está marcado para limpeza.</summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>Severidade sugerida para colorização.</summary>
    public Severity Severity { get; init; } = Severity.Low;

    /// <summary>Tamanho formatado.</summary>
    public string SizeFormatted => ByteFormat.Format(EstimatedBytes);
}

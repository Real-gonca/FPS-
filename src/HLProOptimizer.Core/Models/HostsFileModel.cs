namespace HLProOptimizer.Core.Models;

/// <summary>Modelo do arquivo hosts carregado para edição.</summary>
public sealed class HostsFileModel
{
    /// <summary>Caminho completo do arquivo (System32\drivers\etc\hosts).</summary>
    public required string FilePath { get; init; }

    /// <summary>Todas as linhas (entradas + comentários).</summary>
    public IReadOnlyList<HostsEntry> Entries { get; init; } = [];

    /// <summary>Se o arquivo é somente leitura.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Caminho do backup gerado antes da última modificação.</summary>
    public string? BackupPath { get; init; }

    /// <summary>Última modificação do arquivo.</summary>
    public DateTime? LastModified { get; init; }

    /// <summary>Domínios atualmente bloqueados.</summary>
    public IReadOnlyList<string> BlockedHostnames =>
        Entries.Where(e => !e.IsCommentOnly && e.IsBlocked).Select(e => e.Hostname).ToList();
}

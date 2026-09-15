namespace HLProOptimizer.Core.Models;

/// <summary>Uma linha do arquivo hosts.</summary>
public sealed class HostsEntry
{
    /// <summary>Endereço IP (ex.: "0.0.0.0" para bloqueio).</summary>
    public required string IpAddress { get; set; }

    /// <summary>Hostname/domínio.</summary>
    public required string Hostname { get; set; }

    /// <summary>Comentário da linha (após '#'), sem o caractere.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Se a linha é um bloqueio ativo (IP nulo/loopback).</summary>
    public bool IsBlocked { get; set; }

    /// <summary>Linha original lida do arquivo (preservada para round-trip fiel).</summary>
    public string? RawLine { get; set; }

    /// <summary>Se a linha é apenas comentário ou está vazia.</summary>
    public bool IsCommentOnly { get; set; }

    /// <summary>Serializa a entrada de volta para o formato do arquivo hosts.</summary>
    public string ToHostsLine()
    {
        if (IsCommentOnly)
        {
            return RawLine ?? $"# {Comment}";
        }

        var comment = string.IsNullOrWhiteSpace(Comment) ? string.Empty : $" # {Comment}";
        return $"{IpAddress,-16}{Hostname}{comment}";
    }
}

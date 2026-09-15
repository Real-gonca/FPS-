using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>
/// Registro de uma ação executada pelo usuário - alimenta "Ações Recentes" do
/// Dashboard e o relatório completo. Persistido em SQLite via EF Core.
/// </summary>
public sealed class ActionRecord
{
    /// <summary>Identificador (GUID).</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Momento da ação.</summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>Tipo da ação.</summary>
    public ActionKind Kind { get; init; }

    /// <summary>Título curto (ex.: "Otimização Gamer").</summary>
    public required string Title { get; init; }

    /// <summary>Detalhes/resultados.</summary>
    public string Details { get; init; } = string.Empty;

    /// <summary>Bytes afetados (liberados/backup).</summary>
    public long BytesAffected { get; init; }

    /// <summary>Duração em milissegundos.</summary>
    public long DurationMs { get; init; }

    /// <summary>Se a ação foi bem-sucedida.</summary>
    public bool Success { get; init; } = true;

    /// <summary>Marcadores adicionais em JSON (ex.: passos da otimização).</summary>
    public string? PayloadJson { get; init; }

    /// <summary>Bytes afetados formatados.</summary>
    public string BytesFormatted => BytesAffected > 0 ? ByteFormat.Format(BytesAffected) : "-";

    /// <summary>Duração formatada.</summary>
    public string DurationFormatted => TimeSpan.FromMilliseconds(DurationMs).ToString(@"mm\:ss");
}

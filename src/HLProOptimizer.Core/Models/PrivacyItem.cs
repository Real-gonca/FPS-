using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>
/// Item de privacidade controlável (telemetria, rastreamento, apps em segundo
/// plano, permissões). O estado é sempre do ponto de vista da PROTEÇÃO:
/// <c>IsProtected = true</c> significa que o rastreamento está desativado.
/// </summary>
public sealed class PrivacyItem
{
    /// <summary>Identificador estável (ex.: "telemetry.diagtrack").</summary>
    public required string Id { get; init; }

    /// <summary>Nome exibido.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Explicação do que o item faz e do efeito de desativá-lo.</summary>
    public required string Description { get; init; }

    /// <summary>Grupo do item.</summary>
    public PrivacyCategory Category { get; init; }

    /// <summary>Se a proteção está atualmente aplicada.</summary>
    public bool IsProtected { get; set; }

    /// <summary>Se aplicar a proteção é recomendado pelo HL PRO OPTIMIZER.</summary>
    public bool IsRecommended { get; set; } = true;

    /// <summary>Se exige administrador.</summary>
    public bool RequiresAdmin { get; init; }

    /// <summary>Aviso de risco (ex.: quebra apps da Store).</summary>
    public string RiskNote { get; init; } = string.Empty;

    /// <summary>Se é reversível pela ação "Restaurar Padrões".</summary>
    public bool IsReversible { get; init; } = true;

    /// <summary>Impacto estimado em desempenho quando protegido (0-5).</summary>
    public int PerformanceGainScore { get; init; }
}

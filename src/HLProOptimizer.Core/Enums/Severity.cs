namespace HLProOptimizer.Core.Enums;

/// <summary>
/// Gravidade de um problema encontrado pela análise do sistema.
/// Determina cor, ordenação e urgência exibida na UI.
/// </summary>
public enum Severity
{
    /// <summary>Informativo - nenhuma ação necessária.</summary>
    Informational = 0,

    /// <summary>Impacto baixo (ex.: poucos MB de lixo).</summary>
    Low = 1,

    /// <summary>Impacto médio (ex.: programas de inicialização desnecessários).</summary>
    Medium = 2,

    /// <summary>Impacto alto (ex.: telemetria ativa, disco quase cheio).</summary>
    High = 3,

    /// <summary>Crítico (ex.: serviço essencial parado, disco &gt; 95% cheio).</summary>
    Critical = 4
}

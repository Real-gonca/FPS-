namespace Nexus.Domain.Optimization;

/// <summary>Nível de risco apresentado no badge da UI (baixo/médio/alto).</summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,
}

/// <summary>Ciclo de vida de uma ação de otimização registada no histórico.</summary>
public enum OptimizationStatus
{
    Pending,
    Running,
    Success,
    Failed,
    /// <summary>A ação falhou (ou foi desfeita) e o rollback foi aplicado.</summary>
    RolledBack,
    Skipped,
    /// <summary>Bloqueada antes da aplicação (ex.: sem privilégios de administrador).</summary>
    Blocked,
}

/// <summary>
/// Visibilidade do item por Modo: <see cref="Simple"/> = visível nos dois modos;
/// <see cref="Advanced"/> = apenas no Modo Avançado. O Modo Simples esconde
/// tudo o que é Advanced — inclusive em listas pesquisáveis (spec §7).
/// </summary>
public enum FeatureVisibility
{
    Simple,
    Advanced,
}

/// <summary>Tipo de alteração efetuada — determina o mecanismo de backup/rollback.</summary>
public enum ChangeKind
{
    /// <summary>Chave/valor de registry (Target = caminho, Detail = nome do valor).</summary>
    Registry,
    /// <summary>Estado/configuração de serviço Windows (Target = nome do serviço).</summary>
    Service,
    /// <summary>Ficheiro/pasta (Target = caminho completo).</summary>
    File,
}

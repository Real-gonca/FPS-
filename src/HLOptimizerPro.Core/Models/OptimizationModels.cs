namespace HLOptimizerPro.Core.Models;

/// <summary>
/// Uma alteração concreta e reversível ao sistema.
///
/// Regras de ouro implementadas por este modelo:
///  1. <see cref="Apply"/> guarda SEMPRE o valor anterior em <see cref="Backup"/> antes de escrever.
///  2. <see cref="Revert"/> repõe exatamente o valor guardado (ou remove a chave que foi criada).
///  3. <see cref="SimulationOnly"/> permite o "modo Simular": nada é escrito no sistema.
///  4. <see cref="Risk"/> + <see cref="RequiresConfirmation"/> alimentam o diálogo de confirmação.
/// </summary>
public sealed class OptimizationAction
{
    /// <summary>Chave única e estável (usada no diário de alterações e nas recomendações).</summary>
    public required string Key { get; init; }

    public required string Title { get; init; }
    public required string Description { get; init; }

    /// <summary>O que exatamente será alterado, em linguagem técnica (mostrado no modo Simular).</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>Valor atual detetado antes de aplicar (para comparação visual).</summary>
    public string CurrentValue { get; init; } = "—";

    /// <summary>Valor que será escrito ao aplicar.</summary>
    public string NewValue { get; init; } = "—";

    public OptimizationCategory Category { get; init; } = OptimizationCategory.Sistema;

    public OptimizationArea Area { get; init; } = OptimizationArea.DesempenhoSistema;

    /// <summary>Risco explicado ao utilizador ("Baixo — apenas latência", "Médio — afeta telemetria"...).</summary>
    public string Risk { get; init; } = "Baixo — reversível";

    /// <summary>Nível de risco para colorir o cartão.</summary>
    public HealthLevel RiskLevel { get; init; } = HealthLevel.Otimo;

    /// <summary>Exige ponto de restauração + confirmação explícita do utilizador.</summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>Requer reinício do sistema para ter efeito.</summary>
    public bool RequiresRestart { get; init; }

    /// <summary>Requer privilégios de administrador.</summary>
    public bool RequiresElevation { get; init; } = true;

    /// <summary>Estado atual relativamente ao perfil/otimização em curso.</summary>
    public bool IsApplied { get; internal set; }

    /// <summary>Não disponível nesta máquina (ex.: chave inexistente, hardware sem suporte).</summary>
    public bool IsAvailable { get; init; } = true;

    public string? UnavailableReason { get; init; }

    /// <summary>Aplicação real (grava o backup e altera o sistema).</summary>
    public Action<OptimizationAction>? Apply { get; init; }

    /// <summary>Reversão real (repõe o backup).</summary>
    public Func<OptimizationAction, Result>? Revert { get; init; }

    /// <summary>Verificação opcional: lê o sistema e diz se a otimização já está aplicada.</summary>
    public Func<bool>? IsAlreadyApplied { get; init; }

    /// <summary>Chamado no modo Simular — devolve a descrição do que mudaria.</summary>
    public Func<string>? SimulateExplanation { get; init; }

    /// <summary>Valor original guardado no momento do Apply (base da reversão).</summary>
    public Dictionary<string, string?> Backup { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Quando foi aplicada/ revertida (auditoria).</summary>
    public DateTime? AppliedAt { get; internal set; }
    public DateTime? RevertedAt { get; internal set; }

    public string StatusText => !IsAvailable
        ? "Indisponível"
        : IsApplied ? "Aplicado" : "Pronto";

    public string RestartText => RequiresRestart ? "Requer reinício" : "Efeito imediato";

    public string SimulationText => SimulateExplanation?.Invoke()
        ?? $"Em modo Simular: seria alterado «{Target}» de «{CurrentValue}» para «{NewValue}».";
}

/// <summary>Perfil de tweaks (Competitivo, Streaming, Balanceado, Extremo).</summary>
public sealed class TweakProfileDefinition
{
    public TweakProfile Profile { get; init; }
    public required string Name { get; init; }
    public required string Summary { get; init; }
    public required string Description { get; init; }
    public string IconKind { get; init; } = "TuneVariant";
    public string RecommendedFor { get; init; } = string.Empty;

    /// <summary>O que será ajustado neste perfil (lista do briefing).</summary>
    public IReadOnlyList<string> Adjustments { get; init; } = Array.Empty<string>();

    /// <summary>Chaves das ações do catálogo aplicadas por este perfil.</summary>
    public IReadOnlyList<string> ActionKeys { get; init; } = Array.Empty<string>();

    /// <summary>Aviso de risco (o perfil Extremo altera coisas sensíveis).</summary>
    public string Warning { get; init; } = string.Empty;
}

/// <summary>Registo do diário de alterações — permite "Reverter Tudo" com segurança.</summary>
public sealed class JournalEntry
{
    public string ActionKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; } = DateTime.Now;
    public DateTime? RevertedAt { get; set; }
    public bool RequiresRestart { get; set; }
    public Dictionary<string, string?> Backup { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsActive => RevertedAt is null;
}

/// <summary>Registo do histórico de atividades (ecrã Configuração e Logs do Sistema).</summary>
public sealed class ActivityEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Category { get; set; } = "Sistema";
    public string Description { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public bool Success { get; set; } = true;
    public bool Simulated { get; set; }

    public string TimeText => Timestamp.ToString("dd/MM HH:mm:ss");
    public string StatusText => Simulated ? "Simulado" : Success ? "Sucesso" : "Falhou";
}

/// <summary>Contagem de itens resultante de uma limpeza (usada no resumo visual).</summary>
public sealed class CleanupSummary
{
    public long FilesDeleted { get; set; }
    public long BytesFreed { get; set; }
    public long RegistryEntriesRemoved { get; set; }
    public long CacheEntriesCleared { get; set; }
    public IReadOnlyList<string> Details { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    public void Add(CleanupSummary other)
    {
        FilesDeleted += other.FilesDeleted;
        BytesFreed += other.BytesFreed;
        RegistryEntriesRemoved += other.RegistryEntriesRemoved;
        CacheEntriesCleared += other.CacheEntriesCleared;
        Details = Details.Concat(other.Details).ToList();
        Errors = Errors.Concat(other.Errors).ToList();
    }
}

/// <summary>Tipo de limpeza disponível no módulo "Limpeza Rápida".</summary>
public sealed class CleanupTarget
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string IconKind { get; init; } = "Broom";
    public bool SelectedByDefault { get; init; } = true;

    /// <summary>Pastas a limpar (recursivo, apenas ficheiros temporários).</summary>
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

    /// <summary>Limpezas especiais (DNS, miniaturas, ícones, paper bin...).</summary>
    public CleanupKind Kind { get; init; } = CleanupKind.Files;
}

public enum CleanupKind
{
    Files,
    DnsCache,
    ThumbnailCache,
    IconCache,
    RecycleBin,
    WindowsUpdateCache,
    Prefetch,
    FontCache,
    EventLogs,
}

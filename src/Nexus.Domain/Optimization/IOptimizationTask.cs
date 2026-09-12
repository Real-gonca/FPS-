namespace Nexus.Domain.Optimization;

/// <summary>
/// Contrato de uma tarefa de otimização (pipeline da spec §2.2).
///
/// Regras de segurança:
/// - a tarefa DECLARA as alterações via <see cref="DescribeChanges"/> antes de
///   as efetuar — o backup é feito pelo orquestrador, nunca pela tarefa;
/// - <see cref="ApplyAsync"/> só deve escrever em fontes reais (registry,
///   serviços via CommandExecutor, ficheiros);
/// - sem backup suportado para o ChangeKind → o orquestrador rejeita a tarefa
///   (reversibilidade total, spec §1).
/// </summary>
public interface IOptimizationTask
{
    /// <summary>Chave estável e única (usado por recomendações e histórico).</summary>
    string Key { get; }

    string Name { get; }

    /// <summary>Efeito esperado, porquê é seguro e como reverte — exibido na UI.</summary>
    string Description { get; }

    /// <summary>Ligação de documentação (obrigatório documentar cada tweak).</summary>
    string? DocumentationUrl { get; }

    RiskLevel Risk { get; }

    bool Reversible { get; }

    /// <summary>Se true, a execução exige processo elevado (ver IElevationService).</summary>
    bool RequiresElevation { get; }

    FeatureVisibility Visibility { get; }

    TimeSpan EstimatedDuration { get; }

    IReadOnlyList<ChangeDescriptor> DescribeChanges();

    /// <summary>Aplica a alteração real. Lança em caso de falha (rollback automático).</summary>
    Task ApplyAsync(CancellationToken ct = default);
}

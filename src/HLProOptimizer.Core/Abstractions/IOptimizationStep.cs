using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Passo atômico de otimização (padrão Command). A composição dos modos
/// (Rápida/Completa/Gamer/Privacidade) é feita selecionando os passos cujo
/// <see cref="SupportedModes"/> contém o modo solicitado e cujo
/// <see cref="MinimumLevel"/> é compatível com o nível escolhido.
/// </summary>
public interface IOptimizationStep
{
    /// <summary>Identificador único do passo (ex.: "opt.dns.flush").</summary>
    string Id { get; }

    /// <summary>Nome legível exibido no log em tempo real.</summary>
    string Name { get; }

    /// <summary>Descrição do efeito produzido.</summary>
    string Description { get; }

    /// <summary>Modos em que o passo é executado.</summary>
    IReadOnlyCollection<OptimizationMode> SupportedModes { get; }

    /// <summary>Nível mínimo de agressividade necessário para executar o passo.</summary>
    OptimizationLevel MinimumLevel { get; }

    /// <summary>Ordem de execução dentro do modo.</summary>
    int Order { get; }

    /// <summary>Indica se o passo exige administrador.</summary>
    bool RequiresAdmin { get; }

    /// <summary>Indica se o passo é reversível (aparece em "Restaurar Padrões").</summary>
    bool IsReversible { get; }

    /// <summary>Executa o passo.</summary>
    /// <param name="context">Contexto compartilhado da otimização.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<OptimizationStepResult> ExecuteAsync(OptimizationContext context, CancellationToken cancellationToken = default);

    /// <summary>Desfaz o passo (quando <see cref="IsReversible"/>).</summary>
    /// <param name="context">Contexto compartilhado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default);
}

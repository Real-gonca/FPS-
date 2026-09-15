using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Gerenciador de programas de inicialização.</summary>
public interface IStartupManager
{
    /// <summary>Lista os programas de inicialização (registro, pastas e tarefas agendadas).</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<StartupProgram>> GetStartupProgramsAsync(CancellationToken cancellationToken = default);

    /// <summary>Ativa ou desativa um item (Move entre Run e a chave "StartupApproved"/backup interno).</summary>
    /// <param name="program">Item de inicialização.</param>
    /// <param name="enabled">Novo estado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> SetEnabledAsync(StartupProgram program, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Remove permanentemente a entrada de inicialização.</summary>
    /// <param name="program">Item de inicialização.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> RemoveAsync(StartupProgram program, CancellationToken cancellationToken = default);

    /// <summary>Calcula as estatísticas de boot a partir da lista de programas.</summary>
    /// <param name="programs">Programas listados.</param>
    StartupSummary Summarize(IReadOnlyList<StartupProgram> programs);

    /// <summary>Monta a URL de pesquisa online do executável (identificação de malware).</summary>
    /// <param name="program">Item de inicialização.</param>
    string BuildSearchUrl(StartupProgram program);

    /// <summary>Desativa em lote os itens considerados não essenciais.</summary>
    /// <param name="programs">Itens candidatos.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Nomes dos itens desativados.</returns>
    Task<IReadOnlyList<string>> DisableNonEssentialAsync(
        IReadOnlyList<StartupProgram> programs,
        CancellationToken cancellationToken = default);
}

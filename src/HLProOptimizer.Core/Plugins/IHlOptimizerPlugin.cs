using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Plugins;

/// <summary>
/// Contrato público de um plugin do HL PRO OPTIMIZER.
/// Um plugin é uma DLL colocada em <c>%AppData%\HLProOptimizer\Plugins</c> que
/// implementa esta interface; ela é descoberta e carregada em runtime.
/// </summary>
public interface IHlOptimizerPlugin
{
    /// <summary>Identificador único (ex.: "hl.plugin.registrydoctor").</summary>
    string Id { get; }

    /// <summary>Nome exibido.</summary>
    string Name { get; }

    /// <summary>Versão do plugin.</summary>
    Version Version { get; }

    /// <summary>Autor.</summary>
    string Author { get; }

    /// <summary>Descrição curta.</summary>
    string Description { get; }

    /// <summary>Categorias que o plugin analisa/otimiza.</summary>
    IReadOnlyCollection<IssueCategory> Categories { get; }

    /// <summary>Indica se o plugin exige administrador.</summary>
    bool RequiresAdmin { get; }

    /// <summary>Inicializa o plugin com o contexto do host.</summary>
    /// <param name="context">Contexto fornecido pelo host.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default);

    /// <summary>Executa a análise própria do plugin.</summary>
    /// <param name="profile">Perfil do sistema.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<AnalysisIssue>> AnalyzeAsync(SystemProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Executa a otimização própria do plugin.</summary>
    /// <param name="options">Opções da otimização em andamento.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<OptimizationStepResult> OptimizeAsync(OptimizationOptions options, CancellationToken cancellationToken = default);

    /// <summary>Libera recursos ao desativar/sair.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

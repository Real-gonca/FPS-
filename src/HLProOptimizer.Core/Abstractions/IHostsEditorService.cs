using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Editor do arquivo hosts com bloqueio de domínios de anúncios/rastreamento.</summary>
public interface IHostsEditorService
{
    /// <summary>Domínios de telemetria/anúncios conhecidos pelo produto.</summary>
    IReadOnlyList<string> KnownTrackerDomains { get; }

    /// <summary>Carrega o arquivo hosts.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<HostsFileModel> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Salva o arquivo hosts (cria backup automático antes).</summary>
    /// <param name="model">Modelo editado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> SaveAsync(HostsFileModel model, CancellationToken cancellationToken = default);

    /// <summary>Adiciona bloqueios (0.0.0.0) para os domínios informados.</summary>
    /// <param name="domains">Domínios a bloquear.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Quantidade de domínios efetivamente adicionados.</returns>
    Task<int> BlockDomainsAsync(IEnumerable<string> domains, CancellationToken cancellationToken = default);

    /// <summary>Remove bloqueios dos domínios informados.</summary>
    /// <param name="domains">Domínios a liberar.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Quantidade de domínios efetivamente removidos.</returns>
    Task<int> UnblockDomainsAsync(IEnumerable<string> domains, CancellationToken cancellationToken = default);

    /// <summary>Exporta o conteúdo atual para um arquivo.</summary>
    /// <param name="destinationFile">Caminho de destino.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<string> ExportAsync(string destinationFile, CancellationToken cancellationToken = default);

    /// <summary>Importa uma lista de domínios (um por linha, '#' = comentário).</summary>
    /// <param name="sourceFile">Arquivo de origem.</param>
    /// <param name="blockImported">Se os domínios importados devem ser bloqueados.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<int> ImportAsync(string sourceFile, bool blockImported = true, CancellationToken cancellationToken = default);

    /// <summary>Restaura o último backup criado.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> RestoreBackupAsync(CancellationToken cancellationToken = default);
}

using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Plugins;

/// <summary>Descoberta, carregamento e ciclo de vida dos plugins.</summary>
public interface IPluginManager
{
    /// <summary>Plugins carregados e ativos.</summary>
    IReadOnlyList<IHlOptimizerPlugin> LoadedPlugins { get; }

    /// <summary>Descritores de todos os plugins encontrados (ativos ou não).</summary>
    IReadOnlyList<PluginDescriptor> Descriptors { get; }

    /// <summary>Varre o diretório de plugins e carrega os habilitados.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Habilita um plugin (persiste a escolha e o carrega).</summary>
    /// <param name="pluginId">Identificador do plugin.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> EnableAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>Desativa um plugin (persiste a escolha e o descarrega).</summary>
    /// <param name="pluginId">Identificador do plugin.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> DisableAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>Instala um plugin a partir de uma DLL (copia para a pasta de plugins).</summary>
    /// <param name="sourceDllPath">Caminho da DLL.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PluginDescriptor?> InstallAsync(string sourceDllPath, CancellationToken cancellationToken = default);

    /// <summary>Remove um plugin instalado.</summary>
    /// <param name="pluginId">Identificador do plugin.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> UninstallAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>Descarrega todos os plugins (shutdown do app).</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

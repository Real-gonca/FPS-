using System.Reflection;
using System.Runtime.Loader;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Core.Plugins;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Plugins;

/// <summary>
/// Descoberta e carregamento de plugins a partir de
/// <c>%AppData%\HLProOptimizer\Plugins\*.dll</c>.
/// </summary>
/// <remarks>
/// <para>
/// Um plugin é qualquer DLL que exponha uma classe pública com construtor sem
/// parâmetros implementando <see cref="IHlOptimizerPlugin"/>. O carregamento é
/// defensivo: uma DLL inválida nunca impede o aplicativo de iniciar - ela é
/// marcada como <see cref="PluginState.Failed"/> e o erro é logado.
/// </para>
/// <para>
/// O estado habilitado/desabilitado é persistido em <c>plugins.json</c> no
/// diretório de dados.
/// </para>
/// </remarks>
public sealed class DiskPluginManager : IPluginManager
{
    private const string StateFileName = "plugins.json";

    private readonly ISystemPaths _paths;
    private readonly ISystemInformationService _systemInformation;
    private readonly IRegistryService _registry;
    private readonly IFileSystemService _fileSystem;
    private readonly ICommandRunner _commandRunner;
    private readonly IServiceManager _services;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DiskPluginManager> _logger;
    private readonly List<IHlOptimizerPlugin> _loaded = [];
    private readonly List<PluginDescriptor> _descriptors = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, bool> _enabledMap = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadedOnce;

    /// <summary>Cria o gerenciador de plugins.</summary>
    public DiskPluginManager(
        ISystemPaths paths,
        ISystemInformationService systemInformation,
        IRegistryService registry,
        IFileSystemService fileSystem,
        ICommandRunner commandRunner,
        IServiceManager services,
        ILoggerFactory loggerFactory)
    {
        _paths = paths;
        _systemInformation = systemInformation;
        _registry = registry;
        _fileSystem = fileSystem;
        _commandRunner = commandRunner;
        _services = services;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DiskPluginManager>();
    }

    /// <inheritdoc />
    public IReadOnlyList<IHlOptimizerPlugin> LoadedPlugins => _loaded;

    /// <inheritdoc />
    public IReadOnlyList<PluginDescriptor> Descriptors => _descriptors;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_loadedOnce)
            {
                return;
            }

            _paths.EnsureCreated();
            _enabledMap = LoadState();

            var directory = _paths.PluginDirectory;

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var files = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);

            _logger.LogInformation("Verificando {Count} DLL(s) na pasta de plugins.", files.Length);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await LoadPluginAsync(file, cancellationToken).ConfigureAwait(false);
            }

            _loadedOnce = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao carregar plugins.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> EnableAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _enabledMap[pluginId] = true;
            SaveState();

            var descriptor = _descriptors.FirstOrDefault(d => d.Id == pluginId);

            if (descriptor is not null && _loaded.All(p => p.Id != pluginId))
            {
                await LoadPluginAsync(descriptor.FilePath, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Plugin '{PluginId}' habilitado.", pluginId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao habilitar o plugin '{PluginId}'.", pluginId);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DisableAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _enabledMap[pluginId] = false;
            SaveState();

            var plugin = _loaded.FirstOrDefault(p => string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase));

            if (plugin is not null)
            {
                await plugin.ShutdownAsync(cancellationToken).ConfigureAwait(false);
                _loaded.Remove(plugin);
            }

            _logger.LogInformation("Plugin '{PluginId}' desabilitado.", pluginId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao desabilitar o plugin '{PluginId}'.", pluginId);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PluginDescriptor?> InstallAsync(string sourceDllPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceDllPath))
        {
            _logger.LogWarning("Arquivo de plugin não encontrado: {Path}.", sourceDllPath);
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _paths.EnsureCreated();

            var destination = Path.Combine(_paths.PluginDirectory, Path.GetFileName(sourceDllPath));
            File.Copy(sourceDllPath, destination, overwrite: true);

            _logger.LogInformation("Plugin copiado para {Destination}.", destination);

            var descriptor = await LoadPluginAsync(destination, cancellationToken).ConfigureAwait(false);

            if (descriptor is not null)
            {
                _enabledMap[descriptor.Id] = true;
                SaveState();
            }

            return descriptor;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao instalar o plugin {Path}.", sourceDllPath);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UninstallAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        await DisableAsync(pluginId, cancellationToken).ConfigureAwait(false);

        var descriptor = _descriptors.FirstOrDefault(d =>
            string.Equals(d.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        if (descriptor is null)
        {
            return false;
        }

        try
        {
            if (File.Exists(descriptor.FilePath))
            {
                File.Delete(descriptor.FilePath);
            }

            _descriptors.Remove(descriptor);
            _enabledMap.Remove(pluginId);
            SaveState();

            _logger.LogInformation("Plugin '{PluginId}' desinstalado.", pluginId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao remover o arquivo do plugin '{PluginId}'.", pluginId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        foreach (var plugin in _loaded.ToList())
        {
            try
            {
                await plugin.ShutdownAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao encerrar o plugin '{PluginId}'.", plugin.Id);
            }
        }

        _loaded.Clear();
    }

    /// <summary>Carrega uma DLL e instancia os plugins encontrados.</summary>
    private async Task<PluginDescriptor?> LoadPluginAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(filePath);

            var pluginTypes = assembly
                .GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
                .Where(t => typeof(IHlOptimizerPlugin).IsAssignableFrom(t))
                .ToList();

            if (pluginTypes.Count == 0)
            {
                _logger.LogDebug("Nenhum IHlOptimizerPlugin em {File}.", filePath);
                return null;
            }

            PluginDescriptor? first = null;

            foreach (var type in pluginTypes)
            {
                var descriptor = await InstantiateAsync(type, filePath, cancellationToken).ConfigureAwait(false);

                first ??= descriptor;
            }

            return first;
        }
        catch (BadImageFormatException ex)
        {
            return AddFailedDescriptor(filePath, $"DLL inválida ou de arquitetura incompatível: {ex.Message}");
        }
        catch (ReflectionTypeLoadException ex)
        {
            return AddFailedDescriptor(filePath, $"Dependências ausentes: {ex.LoaderExceptions.FirstOrDefault()?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            return AddFailedDescriptor(filePath, ex.Message);
        }
    }

    /// <summary>Instancia, inicializa e registra um plugin.</summary>
    private async Task<PluginDescriptor?> InstantiateAsync(Type type, string filePath, CancellationToken cancellationToken)
    {
        if (Activator.CreateInstance(type) is not IHlOptimizerPlugin plugin)
        {
            return null;
        }

        var enabled = _enabledMap.TryGetValue(plugin.Id, out var value) ? value : true;

        if (!enabled)
        {
            var disabled = new PluginDescriptor
            {
                Id = plugin.Id,
                Name = plugin.Name,
                Version = plugin.Version.ToString(),
                Author = plugin.Author,
                Description = plugin.Description,
                FilePath = filePath,
                State = PluginState.Disabled
            };

            _descriptors.Add(disabled);

            return disabled;
        }

        try
        {
            var context = new PluginContext(
                typeof(DiskPluginManager).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                new PluginLogger(_loggerFactory.CreateLogger($"Plugin.{plugin.Id}"), plugin.Name),
                _registry,
                _fileSystem,
                _commandRunner,
                _services,
                _systemInformation.IsAdministrator);

            await plugin.InitializeAsync(context, cancellationToken).ConfigureAwait(false);

            _loaded.Add(plugin);

            var descriptor = new PluginDescriptor
            {
                Id = plugin.Id,
                Name = plugin.Name,
                Version = plugin.Version.ToString(),
                Author = plugin.Author,
                Description = plugin.Description,
                FilePath = filePath,
                State = PluginState.Enabled
            };

            _descriptors.Add(descriptor);
            _logger.LogInformation("Plugin '{Name}' v{Version} carregado.", plugin.Name, plugin.Version);

            return descriptor;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao inicializar o plugin '{PluginId}'.", plugin.Id);

            return AddFailedDescriptor(filePath, ex.Message, plugin.Id, plugin.Name);
        }
    }

    /// <summary>Registra um descritor de plugin que falhou ao carregar.</summary>
    private PluginDescriptor AddFailedDescriptor(string filePath, string error, string? id = null, string? name = null)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        var descriptor = new PluginDescriptor
        {
            Id = id ?? $"unknown.{fileName}",
            Name = name ?? fileName,
            FilePath = filePath,
            State = PluginState.Failed,
            LoadError = error
        };

        _descriptors.Add(descriptor);

        return descriptor;
    }

    /// <summary>Carrega o mapa de plugins habilitados.</summary>
    private Dictionary<string, bool> LoadState()
    {
        var file = Path.Combine(_paths.ApplicationDataDirectory, StateFileName);

        if (!File.Exists(file))
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(file);
            var map = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);

            return map is null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(map, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Estado de plugins corrompido; todos serão habilitados por padrão.");
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Persiste o mapa de plugins habilitados.</summary>
    private void SaveState()
    {
        try
        {
            var file = Path.Combine(_paths.ApplicationDataDirectory, StateFileName);
            File.WriteAllText(file, JsonConvert.SerializeObject(_enabledMap, Formatting.Indented));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Não foi possível salvar o estado dos plugins.");
        }
    }
}

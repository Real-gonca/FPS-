using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Plugins;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Plugins;

/// <summary>
/// Implementação de <see cref="IPluginContext"/>: expõe aos plugins apenas a
/// superfície necessária (Interface Segregation), nunca o container de DI.
/// </summary>
public sealed class PluginContext : IPluginContext
{
    /// <summary>Cria o contexto de plugin.</summary>
    public PluginContext(
        string hostVersion,
        IPluginLogger logger,
        IRegistryService registry,
        IFileSystemService fileSystem,
        ICommandRunner commandRunner,
        IServiceManager services,
        bool isElevated)
    {
        HostVersion = hostVersion;
        Logger = logger;
        Registry = registry;
        FileSystem = fileSystem;
        CommandRunner = commandRunner;
        Services = services;
        IsElevated = isElevated;
    }

    /// <inheritdoc />
    public string HostVersion { get; }

    /// <inheritdoc />
    public IPluginLogger Logger { get; }

    /// <inheritdoc />
    public IRegistryService Registry { get; }

    /// <inheritdoc />
    public IFileSystemService FileSystem { get; }

    /// <inheritdoc />
    public ICommandRunner CommandRunner { get; }

    /// <inheritdoc />
    public IServiceManager Services { get; }

    /// <inheritdoc />
    public bool IsElevated { get; }
}

/// <summary>Adaptador de <see cref="ILogger"/> para o logger simplificado dos plugins.</summary>
public sealed class PluginLogger : IPluginLogger
{
    private readonly ILogger _logger;
    private readonly string _pluginName;

    /// <summary>Cria o logger de um plugin.</summary>
    /// <param name="logger">Logger base.</param>
    /// <param name="pluginName">Nome do plugin (prefixo das mensagens).</param>
    public PluginLogger(ILogger logger, string pluginName)
    {
        _logger = logger;
        _pluginName = pluginName;
    }

    /// <inheritdoc />
    public void Info(string message) => _logger.LogInformation("[Plugin {Plugin}] {Message}", _pluginName, message);

    /// <inheritdoc />
    public void Warning(string message) => _logger.LogWarning("[Plugin {Plugin}] {Message}", _pluginName, message);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null)
        => _logger.LogError(exception, "[Plugin {Plugin}] {Message}", _pluginName, message);
}

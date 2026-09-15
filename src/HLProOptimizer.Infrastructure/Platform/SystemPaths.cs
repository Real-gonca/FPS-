using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Platform;

/// <summary>
/// Resolução dos caminhos de dados do aplicativo em <c>%AppData%\HLProOptimizer</c>.
/// </summary>
public sealed class SystemPaths : ISystemPaths
{
    private readonly ILogger<SystemPaths>? _logger;
    private readonly string _root;

    /// <summary>Cria o resolvedor de caminhos.</summary>
    /// <remarks>
    /// O logger é opcional de propósito: o bootstrap do Serilog precisa dos caminhos
    /// ANTES de existir um provedor de logging (o Serilog grava justamente na pasta
    /// resolvida aqui). Fora desse cenário o logger vem da injeção de dependências.
    /// </remarks>
    /// <param name="logger">Logger (opcional).</param>
    public SystemPaths(ILogger<SystemPaths>? logger = null)
    {
        _logger = logger;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _root = Path.Combine(appData, "HLProOptimizer");
    }

    /// <inheritdoc />
    public string ApplicationDataDirectory => _root;

    /// <inheritdoc />
    public string LogDirectory => Path.Combine(_root, "logs");

    /// <inheritdoc />
    public string DatabasePath => Path.Combine(_root, "hloptimizer.db");

    /// <inheritdoc />
    public string BackupDirectory => Path.Combine(_root, "backups");

    /// <inheritdoc />
    public string PluginDirectory => Path.Combine(_root, "plugins");

    /// <inheritdoc />
    public string TempDirectory => Path.Combine(_root, "temp");

    /// <inheritdoc />
    public string SettingsFilePath => Path.Combine(_root, "settings.json");

    /// <inheritdoc />
    public string HostsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers",
        "etc",
        "hosts");

    /// <inheritdoc />
    public string ExecutablePath => Environment.ProcessPath ?? string.Empty;

    /// <inheritdoc />
    public void EnsureCreated()
    {
        foreach (var directory in new[] { _root, LogDirectory, BackupDirectory, PluginDirectory, TempDirectory })
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Não foi possível criar o diretório {Directory}.", directory);
            }
        }
    }
}

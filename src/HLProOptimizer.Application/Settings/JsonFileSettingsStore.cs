using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Settings;

/// <summary>
/// Persistência das configurações em JSON no diretório de dados do aplicativo
/// (<c>%AppData%\HLProOptimizer\settings.json</c>).
/// </summary>
/// <remarks>
/// JSON foi escolhido (em vez de tabela no SQLite) para que as configurações
/// sobrevivam a uma recriação do banco e possam ser exportadas/importadas
/// diretamente pelo usuário. O EF Core continua sendo a fonte de histórico,
/// relatórios e estado do Modo Gamer.
/// </remarks>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        StringEscapeHandling = StringEscapeHandling.EscapeHtml,
        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
    };

    private readonly ISystemPaths _paths;
    private readonly ILogger<JsonFileSettingsStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Cria o store de configurações.</summary>
    /// <param name="paths">Resolução de caminhos do aplicativo.</param>
    /// <param name="logger">Logger.</param>
    public JsonFileSettingsStore(ISystemPaths paths, ILogger<JsonFileSettingsStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var file = _paths.SettingsFilePath;

        if (!File.Exists(file))
        {
            _logger.LogDebug("Arquivo de configurações não encontrado em {Path}; usando padrões.", file);
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var settings = JsonConvert.DeserializeObject<AppSettings>(json, SerializerSettings);

            if (settings is null)
            {
                _logger.LogWarning("Conteúdo de configurações inválido em {Path}.", file);
                return null;
            }

            return Normalize(settings);
        }
        catch (JsonException ex)
        {
            // Arquivo corrompido: preserva o original e volta ao padrão.
            _logger.LogError(ex, "Falha ao desserializar as configurações de {Path}.", file);
            TryPreserveCorruptedFile(file);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Falha de I/O ao ler as configurações de {Path}.", file);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var file = _paths.SettingsFilePath;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _paths.EnsureCreated();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            var json = JsonConvert.SerializeObject(Normalize(settings), SerializerSettings);

            // Escrita atômica: grava em arquivo temporário e substitui.
            var tempFile = file + ".tmp";
            await File.WriteAllTextAsync(tempFile, json, cancellationToken).ConfigureAwait(false);

            if (File.Exists(file))
            {
                File.Replace(tempFile, file, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempFile, file, overwrite: true);
            }

            _logger.LogDebug("Configurações salvas em {Path}.", file);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        var file = _paths.SettingsFilePath;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
                _logger.LogInformation("Configurações removidas ({Path}).", file);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Garante valores coerentes mesmo vindo de um arquivo antigo/editado à mão.</summary>
    private static AppSettings Normalize(AppSettings settings)
    {
        settings.MonitoringIntervalSeconds = settings.MonitoringIntervalSeconds switch
        {
            < 1 => 1,
            > 30 => 30,
            _ => settings.MonitoringIntervalSeconds
        };

        settings.MonitoringHistorySeconds = Math.Clamp(settings.MonitoringHistorySeconds, 10, 600);
        settings.CleanupMinimumFileAgeDays = Math.Max(0, settings.CleanupMinimumFileAgeDays);

        if (string.IsNullOrWhiteSpace(settings.LatencyProbeHost))
        {
            settings.LatencyProbeHost = "8.8.8.8";
        }

        if (string.IsNullOrWhiteSpace(settings.PreferredDnsPreset))
        {
            settings.PreferredDnsPreset = "Cloudflare";
        }

        settings.GameProcessNames ??= [];
        settings.CleanupExclusions ??= [];
        settings.DefaultCleanupTargetIds ??= [];
        settings.CustomBlockedDomains ??= [];

        return settings;
    }

    /// <summary>Renomeia um arquivo corrompido para permitir diagnóstico posterior.</summary>
    private void TryPreserveCorruptedFile(string file)
    {
        try
        {
            var backup = $"{file}.corrupted-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(file, backup, overwrite: true);
            _logger.LogWarning("Configurações corrompidas preservadas em {Backup}.", backup);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível preservar o arquivo de configurações corrompido.");
        }
    }
}

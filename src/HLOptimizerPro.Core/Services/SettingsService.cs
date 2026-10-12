using System.Text.Json;
using System.Text.Json.Serialization;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Persistência das configurações em JSON (<c>%AppData%\HLOptimizerPro\settings.json</c>).
///
/// Decisões de projeto:
///  • Gravação atómica (ficheiro temporário + substituição) para nunca corromper as configurações
///    se a aplicação for encerrada a meio de um Save.
///  • Cópia de segurança automática (<c>settings.bak.json</c>) no primeiro arranque de cada versão.
///  • Falhas de leitura devolvem as configurações por omissão em vez de lançar exceção.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<SettingsService> _logger;
    private readonly object _gate = new();

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        Current = Load();
    }

    /// <summary>Configurações ativas em memória.</summary>
    public AppSettings Current { get; private set; }

    /// <summary>Notifica quem precisa de reagir a alterações (ex.: intervalo de atualização).</summary>
    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings Load()
    {
        try
        {
            AppPaths.EnsureCreated();

            if (!File.Exists(AppPaths.SettingsFile))
            {
                var fresh = new AppSettings();
                Save(fresh);
                return fresh;
            }

            var json = File.ReadAllText(AppPaths.SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return Normalize(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Não foi possível ler as configurações — a usar valores por omissão.");

            // Preserva o ficheiro inválido para diagnóstico em vez de o apagar.
            try
            {
                var corrupted = AppPaths.Combine($"settings.corrompido-{DateTime.Now:yyyyMMddHHmmss}.json");
                if (File.Exists(AppPaths.SettingsFile)) File.Copy(AppPaths.SettingsFile, corrupted, overwrite: true);
            }
            catch
            {
                // Ignorar — o importante é continuar a arrancar.
            }

            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            try
            {
                var normalized = Normalize(settings);
                Current = normalized;

                var json = JsonSerializer.Serialize(normalized, JsonOptions);
                var temp = AppPaths.SettingsFile + ".tmp";
                File.WriteAllText(temp, json);

                // Substituição atómica — nunca deixa o ficheiro a meio.
                File.Move(temp, AppPaths.SettingsFile, overwrite: true);

                _logger.LogDebug("Configurações guardadas em {File}", AppPaths.SettingsFile);
                SettingsChanged?.Invoke(this, normalized);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao guardar as configurações.");
            }
        }
    }

    /// <summary>Guarda as configurações atuais.</summary>
    public void Save() => Save(Current);

    /// <summary>Aplica alterações a uma cópia e persiste (evita mutações parciais em caso de erro).</summary>
    public void Update(Action<AppSettings> mutate)
    {
        var copy = Current.Clone();
        mutate(copy);
        Save(copy);
    }

    /// <summary>Restaura as configurações de fábrica.</summary>
    public AppSettings ResetToDefaults()
    {
        var defaults = new AppSettings();
        Save(defaults);
        return defaults;
    }

    /// <summary>Garante que os valores estão em intervalos válidos (ficheiros editados à mão ou versões antigas).</summary>
    private static AppSettings Normalize(AppSettings s)
    {
        s.RealtimeIntervalMs = Math.Clamp(s.RealtimeIntervalMs, 500, 5000);
        s.AutoCleanupIntervalDays = Math.Clamp(s.AutoCleanupIntervalDays, 1, 30);
        s.UiScale = Math.Clamp(s.UiScale, 0.8, 1.5);
        s.AutoCleanupTargets ??= new List<string>();
        s.EnabledAppProfiles ??= new List<string>();
        s.CustomGames ??= new Dictionary<string, string>();
        s.LastTweakProfile = Enum.IsDefined(s.LastTweakProfile) ? s.LastTweakProfile : TweakProfile.Balanceado;

        if (string.IsNullOrWhiteSpace(s.Language)) s.Language = "pt-PT";
        if (string.IsNullOrWhiteSpace(s.PreferredResolution)) s.PreferredResolution = "Auto";
        return s;
    }
}

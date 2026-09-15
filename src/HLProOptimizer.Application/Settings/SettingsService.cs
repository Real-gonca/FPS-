using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Settings;

/// <summary>
/// Serviço de configurações com cache em memória, persistência assíncrona e
/// notificação de mudanças para toda a aplicação.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly ISettingsStore _store;
    private readonly ILocalizationService _localization;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _current = AppSettings.CreateDefault();
    private bool _loaded;

    /// <summary>Cria o serviço de configurações.</summary>
    /// <param name="store">Persistência das configurações.</param>
    /// <param name="localization">Serviço de localização (idioma vem das configurações).</param>
    /// <param name="logger">Logger.</param>
    public SettingsService(
        ISettingsStore store,
        ILocalizationService localization,
        ILogger<SettingsService> logger)
    {
        _store = store;
        _localization = localization;
        _logger = logger;
    }

    /// <inheritdoc />
    public AppSettings Current => _current;

    /// <inheritdoc />
    public event EventHandler<AppSettings>? SettingsChanged;

    /// <inheritdoc />
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_loaded)
            {
                return _current;
            }

            var stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

            if (stored is null)
            {
                _current = AppSettings.CreateDefault();
                await _store.SaveAsync(_current, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Configurações padrão criadas no primeiro uso.");
            }
            else
            {
                _current = stored;
                _logger.LogInformation(
                    "Configurações carregadas (idioma={Language}, tema={Theme}, nível={Level}).",
                    _current.Language, _current.Theme, _current.OptimizationLevel);
            }

            _localization.SetLanguage(_current.Language);
            _loaded = true;

            return _current;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao carregar configurações; usando valores padrão.");
            _current = AppSettings.CreateDefault();
            _loaded = true;
            return _current;
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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _current = settings;
            _loaded = true;
            _localization.SetLanguage(settings.Language);
            await _store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(this, _current);
    }

    /// <inheritdoc />
    public async Task<AppSettings> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var defaults = AppSettings.CreateDefault();

        // Preserva o idioma escolhido: restaurar padrões não deve trocar a língua da UI.
        defaults.Language = _current.Language;

        await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Configurações restauradas para o padrão de fábrica.");

        return defaults;
    }

    /// <inheritdoc />
    public async Task SetLanguageAsync(AppLanguage language, CancellationToken cancellationToken = default)
    {
        if (_current.Language == language)
        {
            _localization.SetLanguage(language);
            return;
        }

        var updated = _current.Clone();
        updated.Language = language;
        await SaveAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetThemeAsync(ThemeMode theme, CancellationToken cancellationToken = default)
    {
        if (_current.Theme == theme)
        {
            return;
        }

        var updated = _current.Clone();
        updated.Theme = theme;
        await SaveAsync(updated, cancellationToken).ConfigureAwait(false);
    }
}

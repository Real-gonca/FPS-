using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Serviço de configurações do aplicativo (cache + persistência + notificação).</summary>
public interface ISettingsService
{
    /// <summary>Configurações atualmente carregadas (nunca nulo).</summary>
    AppSettings Current { get; }

    /// <summary>Disparado após qualquer alteração persistida.</summary>
    event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>Carrega as configurações (disco/SQLite), criando padrões quando ausentes.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persiste as configurações e notifica os assinantes.</summary>
    /// <param name="settings">Novo conjunto de configurações.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Restaura as configurações de fábrica.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<AppSettings> ResetToDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>Altera o idioma da aplicação (persiste e notifica a localização).</summary>
    /// <param name="language">Novo idioma.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task SetLanguageAsync(AppLanguage language, CancellationToken cancellationToken = default);

    /// <summary>Altera o tema da aplicação.</summary>
    /// <param name="theme">Novo tema.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task SetThemeAsync(ThemeMode theme, CancellationToken cancellationToken = default);
}

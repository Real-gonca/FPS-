using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Localization;

/// <summary>
/// Implementação de <see cref="ILocalizationService"/> baseada nos recursos
/// incorporados <c>Resources/Strings.resx</c> (neutro = pt-BR),
/// <c>Strings.en.resx</c> e <c>Strings.es.resx</c>.
/// </summary>
/// <remarks>
/// A troca de idioma em runtime altera <see cref="CultureInfo.DefaultThreadCurrentUICulture"/>,
/// de modo que todos os textos passam a ser resolvidos no novo idioma. A camada
/// de Presentation escuta <see cref="LanguageChanged"/> para invalidar bindings
/// (padrão indexer + INotifyPropertyChanged).
/// </remarks>
public sealed class LocalizationService : ILocalizationService
{
    /// <summary>Nome base do recurso incorporado.</summary>
    public const string ResourceBaseName = "HLProOptimizer.Application.Resources.Strings";

    private static readonly ResourceManager Resources =
        new(ResourceBaseName, typeof(LocalizationService).Assembly);

    /// <summary>
    /// Prefixo de chave por tipo de enum. Enums fora deste mapa usam o
    /// <see cref="DescriptionAttribute"/> ou o nome humanizado.
    /// </summary>
    private static readonly Dictionary<Type, string> EnumKeyPrefixes = new()
    {
        [typeof(Severity)] = "Severity_",
        [typeof(IssueCategory)] = "Cat_",
        [typeof(OptimizationMode)] = "Opt_Mode_",
        [typeof(OptimizationLevel)] = "Settings_Level_",
        [typeof(StartupImpact)] = "Startup_Impact_",
        [typeof(PrivacyCategory)] = "Privacy_",
        [typeof(ThemeMode)] = "Settings_Theme_",
        [typeof(UpdateChannel)] = "Settings_Channel_",
        [typeof(UpdateFrequency)] = "Settings_Freq_",
        [typeof(SystemHealthStatus)] = "Health_",
        [typeof(ActionKind)] = "Action_",
        [typeof(ServiceStartupKind)] = "Service_Start_",
        [typeof(ServiceState)] = "Service_State_",
        [typeof(StartupLocation)] = "Startup_Loc_",
        [typeof(ProcessPriorityKind)] = "Process_Priority_",
        [typeof(DiskHealth)] = "Disk_Health_",
    };

    private readonly ILogger<LocalizationService>? _logger;
    private AppLanguage _currentLanguage = AppLanguage.PtBr;

    /// <summary>Cria o serviço de localização.</summary>
    /// <param name="logger">Logger opcional.</param>
    public LocalizationService(ILogger<LocalizationService>? logger = null)
    {
        _logger = logger;
        ApplyCulture(_currentLanguage);
    }

    /// <inheritdoc />
    public AppLanguage CurrentLanguage => _currentLanguage;

    /// <inheritdoc />
    public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;

    /// <inheritdoc />
    public IReadOnlyList<AppLanguage> SupportedLanguages { get; } =
    [
        AppLanguage.PtBr,
        AppLanguage.En,
        AppLanguage.Es
    ];

    /// <inheritdoc />
    public event EventHandler<AppLanguage>? LanguageChanged;

    /// <inheritdoc />
    public string this[string key] => GetString(key);

    /// <inheritdoc />
    public string GetString(string key, params object?[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        string? value;

        try
        {
            value = Resources.GetString(key, CultureInfo.CurrentUICulture);
        }
        catch (MissingManifestResourceException ex)
        {
            // Falha de empacotamento dos recursos: não pode derrubar a UI.
            _logger?.LogError(ex, "Recurso de localização não encontrado no assembly para a chave '{Key}'.", key);
            value = null;
        }

        value ??= key;

        if (args is { Length: > 0 })
        {
            try
            {
                return string.Format(CultureInfo.CurrentUICulture, value, args);
            }
            catch (FormatException ex)
            {
                _logger?.LogWarning(ex, "Formato inválido no recurso '{Key}'.", key);
                return value;
            }
        }

        return value;
    }

    /// <inheritdoc />
    public void SetLanguage(AppLanguage language)
    {
        if (_currentLanguage == language)
        {
            return;
        }

        _currentLanguage = language;
        ApplyCulture(language);
        _logger?.LogInformation("Idioma da interface alterado para {Language}.", language);
        LanguageChanged?.Invoke(this, language);
    }

    /// <inheritdoc />
    public string GetEnumText(Enum value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var type = value.GetType();

        if (EnumKeyPrefixes.TryGetValue(type, out var prefix))
        {
            var key = prefix + value;
            var localized = Resources.GetString(key, CultureInfo.CurrentUICulture);
            if (!string.IsNullOrEmpty(localized))
            {
                return localized;
            }
        }

        var description = type
            .GetField(value.ToString())?
            .GetCustomAttribute<DescriptionAttribute>()?
            .Description;

        return string.IsNullOrEmpty(description) ? Humanize(value.ToString()) : description;
    }

    /// <inheritdoc />
    public string GetLanguageDisplayName(AppLanguage language) => language switch
    {
        AppLanguage.PtBr => "Português (Brasil)",
        AppLanguage.En => "English (United States)",
        AppLanguage.Es => "Español",
        _ => language.ToString()
    };

    /// <summary>Aplica a cultura correspondente ao idioma em todas as threads.</summary>
    private static void ApplyCulture(AppLanguage language)
    {
        var culture = language switch
        {
            AppLanguage.PtBr => new CultureInfo("pt-BR"),
            AppLanguage.En => new CultureInfo("en-US"),
            AppLanguage.Es => new CultureInfo("es-ES"),
            _ => CultureInfo.InvariantCulture
        };

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    /// <summary>Converte "CamelCase" em "Camel Case".</summary>
    private static string Humanize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length + 4);

        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }
}

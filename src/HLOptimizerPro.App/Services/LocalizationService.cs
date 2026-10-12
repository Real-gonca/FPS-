using System.Globalization;
using HLOptimizerPro.App.Resources.Localization;
using HLOptimizerPro.Core.Services;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.App.Services;

/// <summary>
/// Gestão de idioma da aplicação (localização via .resx).
///
/// Comportamento: português (pt-PT) por omissão e idioma neutro dos recursos. Alterar o idioma na
/// Configuração é imediato para os textos formatados em código; os textos já desenhados em XAML
/// são atualizados na próxima abertura de página (informamos o utilizador disso na interface).
/// </summary>
public sealed class LocalizationService
{
    private readonly ILogger<LocalizationService> _logger;
    private readonly SettingsService _settings;

    public LocalizationService(ILogger<LocalizationService> logger, SettingsService settings)
    {
        _logger = logger;
        _settings = settings;

        Apply(_settings.Current.Language);
    }

    /// <summary>Código do idioma atual (ex.: "pt-PT").</summary>
    public string CurrentLanguage { get; private set; } = "pt-PT";

    /// <summary>Opções mostradas na Configuração.</summary>
    public static IReadOnlyList<(string Code, string Label)> Options { get; } = Strings.SupportedLanguages
        .Select(kv => (kv.Key, kv.Value))
        .ToList();

    /// <summary>Aplica um idioma e guarda a preferência.</summary>
    public void Apply(string? cultureCode)
    {
        var code = string.IsNullOrWhiteSpace(cultureCode) ? "pt-PT" : cultureCode;

        if (!Strings.SupportedLanguages.ContainsKey(code))
        {
            _logger.LogWarning("Idioma não suportado ({Code}) — a usar português.", code);
            code = "pt-PT";
        }

        Strings.ApplyLanguage(code);
        CurrentLanguage = code;

        _logger.LogInformation("Idioma aplicado: {Code} (formatação: {Format})", code, CultureInfo.CurrentCulture.Name);
    }

    /// <summary>Aplica e persiste a escolha do utilizador.</summary>
    public void SetLanguage(string code)
    {
        Apply(code);
        _settings.Update(s => s.Language = code);
    }

    /// <summary>Texto localizado a partir de uma chave (usado por mensagens dinâmicas).</summary>
    public static string T(string key) => Strings.Get(key);
}

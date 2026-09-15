using System.Globalization;
using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Serviço de localização baseado nos recursos .resx de
/// <c>HLProOptimizer.Application/Resources</c>. Permite trocar o idioma em
/// runtime sem reiniciar o aplicativo.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Idioma atual.</summary>
    AppLanguage CurrentLanguage { get; }

    /// <summary>Cultura efetiva aplicada.</summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>Idiomas suportados.</summary>
    IReadOnlyList<AppLanguage> SupportedLanguages { get; }

    /// <summary>Disparado quando o idioma muda (a UI recarrega os textos).</summary>
    event EventHandler<AppLanguage>? LanguageChanged;

    /// <summary>Obtém um texto pela chave (retorna a própria chave quando não encontrada).</summary>
    /// <param name="key">Chave do recurso.</param>
    string this[string key] { get; }

    /// <summary>Obtém um texto formatado.</summary>
    /// <param name="key">Chave do recurso.</param>
    /// <param name="args">Argumentos do <c>string.Format</c>.</param>
    string GetString(string key, params object?[] args);

    /// <summary>Define o idioma atual e atualiza a cultura das threads.</summary>
    /// <param name="language">Idioma desejado.</param>
    void SetLanguage(AppLanguage language);

    /// <summary>Obtém o texto localizado de um valor de enum (chave "Enum_&lt;Tipo&gt;_&lt;Valor&gt;").</summary>
    /// <param name="value">Valor do enum.</param>
    string GetEnumText(Enum value);

    /// <summary>Nome amigável do idioma (ex.: "Português (Brasil)").</summary>
    /// <param name="language">Idioma.</param>
    string GetLanguageDisplayName(AppLanguage language);
}

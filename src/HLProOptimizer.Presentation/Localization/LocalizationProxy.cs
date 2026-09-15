using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Markup;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Presentation.Localization;

/// <summary>
/// Ponte entre o <see cref="ILocalizationService"/> (camada Application) e o WPF.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que um proxy estático?</b> Markup extensions (<c>{loc:Loc Chave}</c>) são
/// instanciadas pelo parser XAML e não recebem injeção de dependências. O padrão
/// consagrado é um singleton inicializado uma única vez no startup do
/// <c>App.xaml.cs</c> com a instância vinda do container.
/// </para>
/// <para>
/// Quando o idioma muda, o proxy dispara <see cref="PropertyChanged"/> com nome
/// vazio — convenção do WPF para "todas as propriedades mudaram" — e cada binding
/// <c>[Chave]</c> reavalia na hora, sem reiniciar a janela.
/// </para>
/// </remarks>
public sealed class LocalizationProxy : INotifyPropertyChanged
{
    private static readonly LocalizationProxy LazyInstance = new();

    private ILocalizationService? _localization;

    private LocalizationProxy()
    {
    }

    /// <summary>Instância única usada pelos bindings.</summary>
    public static LocalizationProxy Instance => LazyInstance;

    /// <summary>Idioma atual (para ComboBoxes de idioma).</summary>
    public AppLanguage CurrentLanguage => _localization?.CurrentLanguage ?? AppLanguage.PtBr;

    /// <summary>Cultura efetiva (usada por formatadores de data/número).</summary>
    public CultureInfo CurrentCulture => _localization?.CurrentCulture ?? CultureInfo.CurrentUICulture;

    /// <summary>Idiomas suportados.</summary>
    public IReadOnlyList<AppLanguage> SupportedLanguages => _localization?.SupportedLanguages ?? [];

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Vincula o serviço de localização vindo do container de DI.
    /// </summary>
    /// <param name="localization">Serviço de localização.</param>
    public void Initialize(ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        if (ReferenceEquals(_localization, localization))
        {
            return;
        }

        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }

        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;

        RaiseAllPropertiesChanged();
    }

    /// <summary>Texto localizado pela chave (retorna a própria chave quando ausente).</summary>
    /// <param name="key">Chave do recurso.</param>
    public string this[string key] => _localization?[key] ?? key;

    /// <summary>Texto localizado com formatação.</summary>
    /// <param name="key">Chave do recurso.</param>
    /// <param name="args">Argumentos.</param>
    public string GetString(string key, params object?[] args) =>
        _localization?.GetString(key, args) ?? key;

    /// <summary>Texto localizado de um valor de enum.</summary>
    /// <param name="value">Valor do enum.</param>
    public string GetEnumText(Enum value) =>
        _localization?.GetEnumText(value) ?? value.ToString();

    /// <summary>Nome amigável de um idioma.</summary>
    /// <param name="language">Idioma.</param>
    public string GetLanguageDisplayName(AppLanguage language) =>
        _localization?.GetLanguageDisplayName(language) ?? language.ToString();

    /// <summary>Força a releitura de todos os textos (usado após inicialização tardia).</summary>
    public void Refresh() => RaiseAllPropertiesChanged();

    private void OnLanguageChanged(object? sender, AppLanguage language)
    {
        _ = language;

        RaiseAllPropertiesChanged();
    }

    private void RaiseAllPropertiesChanged([CallerMemberName] string? propertyName = null) =>
        // Nome vazio = "todas as propriedades": cada Binding [Chave] reavalia.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? string.Empty));
}

/// <summary>
/// Markup extension de localização: <c>Text="{loc:Loc Dash_Title}"</c>.
/// </summary>
/// <remarks>
/// Devolve um <see cref="Binding"/> (e não uma string fixa) quando o alvo é uma
/// <see cref="System.Windows.DependencyProperty"/>: assim a troca de idioma em
/// runtime atualiza a interface sem recarregar a janela. Em contextos não visuais
/// (ex.: valor de recurso) devolve a string já traduzida.
/// </remarks>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    /// <summary>Cria a extensão sem chave (uso com propriedade explícita).</summary>
    public LocExtension()
    {
    }

    /// <summary>Cria a extensão com a chave do recurso.</summary>
    /// <param name="key">Chave (ex.: "Dash_Title").</param>
    public LocExtension(string key)
    {
        Key = key;
    }

    /// <summary>Chave do recurso localizado.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Formato opcional aplicado ao texto (StringFormat do binding).</summary>
    public string? Format { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return string.Empty;
        }

        // Caminho de indexador: LocalizationProxy.Instance["Chave"].
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationProxy.Instance,
            Mode = BindingMode.OneWay
        };

        if (!string.IsNullOrEmpty(Format))
        {
            binding.StringFormat = Format;
        }

        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target &&
            target.TargetObject is System.Windows.DependencyObject &&
            target.TargetProperty is System.Windows.DependencyProperty)
        {
            // Devolve o binding "vivo" para o WPF assinar as mudanças de idioma.
            return binding.ProvideValue(serviceProvider);
        }

        return LocalizationProxy.Instance[Key];
    }
}

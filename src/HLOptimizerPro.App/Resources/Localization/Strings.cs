using System.Globalization;
using System.Resources;

namespace HLOptimizerPro.App.Resources.Localization;

/// <summary>
/// Acesso tipado aos textos da interface.
///
/// Porque não usamos o gerador do Visual Studio? Porque o projeto tem de compilar também em
/// linha de comandos (<c>dotnet build</c>) e em CI, sem depender do designer de .resx. Esta
/// classe faz exatamente o mesmo trabalho e é verificável no repositório.
///
/// Idioma: português (pt-PT) por omissão. O idioma pode ser alterado na Configuração; quando
/// isso acontece, as janelas em cache são recriadas para refletir os novos textos.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("HLOptimizerPro.App.Resources.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>Cultura atualmente aplicada aos textos.</summary>
    public static CultureInfo Current { get; private set; } = CultureInfo.GetCultureInfo("pt-PT");

    /// <summary>Idiomas suportados (código → nome apresentado na Configuração).</summary>
    public static IReadOnlyDictionary<string, string> SupportedLanguages { get; } = new Dictionary<string, string>
    {
        ["pt-PT"] = "Português (Portugal)",
        ["pt-AO"] = "Português (Angola)",
        ["en-US"] = "English (United States)",
    };

    /// <summary>Aplica um idioma. Devolve false se o idioma não existir.</summary>
    public static bool ApplyLanguage(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode)) return false;

        // A cultura "pt-AO" não tem ficheiro próprio: usa os textos portugueses (idioma neutro).
        var resourceCulture = cultureCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.GetCultureInfo("pt-PT");

        Current = CultureInfo.GetCultureInfo(cultureCode);

        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-PT");   // formatação de números/datas
        CultureInfo.DefaultThreadCurrentUICulture = resourceCulture;
        Thread.CurrentThread.CurrentUICulture = resourceCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("pt-PT");

        return true;
    }

    /// <summary>Lê um recurso (devolve a chave entre parênteses retos se não existir).</summary>
    public static string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;

        try
        {
            var value = Manager.GetString(key, Current);
            return value ?? $"[{key}]";
        }
        catch
        {
            return $"[{key}]";
        }
    }

    // ------------------------------------------------------------------ Aplicação
    public static string App_Name => Get(nameof(App_Name));
    public static string App_Slogan => Get(nameof(App_Slogan));

    // ------------------------------------------------------------------ Navegação
    public static string Nav_Home => Get(nameof(Nav_Home));
    public static string Nav_Assessment => Get(nameof(Nav_Assessment));
    public static string Nav_Tools => Get(nameof(Nav_Tools));
    public static string Nav_Optimizations => Get(nameof(Nav_Optimizations));
    public static string Nav_Recommendations => Get(nameof(Nav_Recommendations));
    public static string Nav_Settings => Get(nameof(Nav_Settings));
    public static string Nav_Help => Get(nameof(Nav_Help));
    public static string Nav_PcBoost => Get(nameof(Nav_PcBoost));
    public static string Nav_About => Get(nameof(Nav_About));
    public static string Nav_Memory => Get(nameof(Nav_Memory));
    public static string Nav_Network => Get(nameof(Nav_Network));
    public static string Nav_AppTuner => Get(nameof(Nav_AppTuner));
    public static string Nav_Tweaks => Get(nameof(Nav_Tweaks));
    public static string Nav_GameMode => Get(nameof(Nav_GameMode));
    public static string Nav_Latency => Get(nameof(Nav_Latency));

    // ------------------------------------------------------------------ Login
    public static string Login_Welcome => Get(nameof(Login_Welcome));
    public static string Login_User => Get(nameof(Login_User));
    public static string Login_Password => Get(nameof(Login_Password));
    public static string Login_Remember => Get(nameof(Login_Remember));
    public static string Login_Forgot => Get(nameof(Login_Forgot));
    public static string Login_SignIn => Get(nameof(Login_SignIn));
    public static string Login_Microsoft => Get(nameof(Login_Microsoft));
    public static string Login_CreateAccount => Get(nameof(Login_CreateAccount));
    public static string Login_Skip => Get(nameof(Login_Skip));
    public static string Login_BenefitsTitle => Get(nameof(Login_BenefitsTitle));

    // ------------------------------------------------------------------ Dashboard
    public static string Dash_OptimizedState => Get(nameof(Dash_OptimizedState));
    public static string Dash_AttentionState => Get(nameof(Dash_AttentionState));
    public static string Dash_RunOptimization => Get(nameof(Dash_RunOptimization));
    public static string Dash_QuickActions => Get(nameof(Dash_QuickActions));
    public static string Dash_QuickCleanup => Get(nameof(Dash_QuickCleanup));
    public static string Dash_GameMode => Get(nameof(Dash_GameMode));
    public static string Dash_RestorePoint => Get(nameof(Dash_RestorePoint));
    public static string Dash_SystemLogs => Get(nameof(Dash_SystemLogs));
    public static string Dash_PerformanceChart => Get(nameof(Dash_PerformanceChart));
    public static string Dash_Uptime => Get(nameof(Dash_Uptime));

    // ------------------------------------------------------------------ Avaliação
    public static string Assessment_Title => Get(nameof(Assessment_Title));
    public static string Assessment_Run => Get(nameof(Assessment_Run));
    public static string Assessment_FullReport => Get(nameof(Assessment_FullReport));
    public static string Assessment_ExportPdf => Get(nameof(Assessment_ExportPdf));
    public static string Assessment_ExportHtml => Get(nameof(Assessment_ExportHtml));

    // ------------------------------------------------------------------ Genéricos
    public static string Common_Apply => Get(nameof(Common_Apply));
    public static string Common_Revert => Get(nameof(Common_Revert));
    public static string Common_Simulate => Get(nameof(Common_Simulate));
    public static string Common_Cancel => Get(nameof(Common_Cancel));
    public static string Common_Close => Get(nameof(Common_Close));
    public static string Common_Refresh => Get(nameof(Common_Refresh));
    public static string Common_Loading => Get(nameof(Common_Loading));
    public static string Common_ApplySelected => Get(nameof(Common_ApplySelected));
    public static string Common_ApplyAll => Get(nameof(Common_ApplyAll));
    public static string Common_RevertAll => Get(nameof(Common_RevertAll));
    public static string Common_Rescan => Get(nameof(Common_Rescan));
    public static string Common_Diagnostics => Get(nameof(Common_Diagnostics));

    // ------------------------------------------------------------------ Segurança
    public static string Safety_RestorePointCreated => Get(nameof(Safety_RestorePointCreated));
    public static string Safety_NotElevated => Get(nameof(Safety_NotElevated));
    public static string Safety_SimulationOn => Get(nameof(Safety_SimulationOn));
    public static string Safety_ConfirmRisky => Get(nameof(Safety_ConfirmRisky));
    public static string Safety_ProtectedProcess => Get(nameof(Safety_ProtectedProcess));
}

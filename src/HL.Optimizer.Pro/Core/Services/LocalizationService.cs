using HL.Optimizer.Pro.Core.Interfaces;
using System.Windows;

namespace HL.Optimizer.Pro.Core.Services;

public class LocalizationService : ILocalizationService
{
    public string CurrentLanguage { get; private set; } = "pt-BR";
    public List<string> AvailableLanguages { get; } = new() { "pt-PT", "pt-BR", "en-US", "es-ES" };

    private Dictionary<string, string> _strings = new();

    public LocalizationService()
    {
        LoadLanguage(CurrentLanguage);
    }

    public void SetLanguage(string culture)
    {
        if (!AvailableLanguages.Contains(culture)) return;
        CurrentLanguage = culture;
        LoadLanguage(culture);

        // Update resource dictionary
        try
        {
            var dict = new ResourceDictionary { Source = new Uri($"Localization/{culture}.xaml", UriKind.Relative) };
            var existing = Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Localization") == true);
            if (existing != null)
            {
                var index = Application.Current.Resources.MergedDictionaries.IndexOf(existing);
                Application.Current.Resources.MergedDictionaries[index] = dict;
            }
        }
        catch { }
    }

    public string GetString(string key)
    {
        return _strings.TryGetValue(key, out var val) ? val : key;
    }

    private void LoadLanguage(string culture)
    {
        // Simplified - in real app load from XAML or resx
        _strings = culture switch
        {
            "en-US" => new Dictionary<string, string>
            {
                { "Dashboard", "Dashboard" },
                { "Booster", "Booster" },
                { "Tweaks", "Tweaks" },
                { "Cleanup", "Cleanup" },
                { "Games", "Games" },
                { "System", "System" },
                { "Diagnostics", "Diagnostics" },
                { "Performance", "Performance Monitor" },
                { "Startup", "Startup" },
                { "Tools", "Advanced Tools" },
                { "Restore", "Restore" },
                { "Settings", "Settings" },
            },
            _ => new Dictionary<string, string>
            {
                { "Dashboard", "Painel Principal" },
                { "Booster", "Booster" },
                { "Tweaks", "Tweaks" },
                { "Cleanup", "Limpeza" },
                { "Games", "Jogos" },
                { "System", "Sistema" },
                { "Diagnostics", "Diagnóstico" },
                { "Performance", "Monitor de Desempenho" },
                { "Startup", "Inicialização" },
                { "Tools", "Ferramentas Avançadas" },
                { "Restore", "Restauração" },
                { "Settings", "Configurações" },
            }
        };
    }
}

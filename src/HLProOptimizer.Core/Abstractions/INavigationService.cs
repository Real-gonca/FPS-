namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Navegação entre as telas da área de conteúdo (ContentControl da MainWindow).
/// A implementação vive na Presentation e trabalha com chaves de navegação,
/// mantendo os ViewModels desacoplados uns dos outros.
/// </summary>
public interface INavigationService
{
    /// <summary>ViewModel atualmente exibido.</summary>
    object? CurrentViewModel { get; }

    /// <summary>Chave de navegação atual.</summary>
    string? CurrentKey { get; }

    /// <summary>Disparado após uma navegação concluída.</summary>
    event EventHandler<NavigationEventArgs>? Navigated;

    /// <summary>Navega para uma tela pela chave.</summary>
    /// <param name="navigationKey">Chave registrada (ex.: <see cref="NavigationKeys.Dashboard"/>).</param>
    /// <param name="parameter">Parâmetro opcional passado ao ViewModel destino.</param>
    /// <returns>True quando a navegação foi efetuada.</returns>
    bool NavigateTo(string navigationKey, object? parameter = null);

    /// <summary>Indica se é possível voltar.</summary>
    bool CanGoBack { get; }

    /// <summary>Volta para a tela anterior.</summary>
    void GoBack();
}

/// <summary>Argumentos do evento de navegação.</summary>
/// <param name="PreviousKey">Chave da tela anterior.</param>
/// <param name="NewKey">Chave da nova tela.</param>
/// <param name="ViewModel">ViewModel instanciado.</param>
public sealed record NavigationEventArgs(string? PreviousKey, string NewKey, object ViewModel);

/// <summary>Chaves de navegação estáveis (evita strings mágicas espalhadas).</summary>
public static class NavigationKeys
{
    /// <summary>Painel principal.</summary>
    public const string Dashboard = "dashboard";

    /// <summary>Análise do sistema.</summary>
    public const string Analysis = "analysis";

    /// <summary>Otimização.</summary>
    public const string Optimization = "optimization";

    /// <summary>Modo Gamer.</summary>
    public const string GameMode = "gamemode";

    /// <summary>Limpeza.</summary>
    public const string Cleanup = "cleanup";

    /// <summary>Gerenciador de inicialização.</summary>
    public const string Startup = "startup";

    /// <summary>Privacidade.</summary>
    public const string Privacy = "privacy";

    /// <summary>Monitoramento em tempo real.</summary>
    public const string Monitoring = "monitoring";

    /// <summary>Ferramentas.</summary>
    public const string Tools = "tools";

    /// <summary>Configurações.</summary>
    public const string Settings = "settings";
}

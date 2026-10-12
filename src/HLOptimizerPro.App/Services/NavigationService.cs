using HLOptimizerPro.App.ViewModels;

namespace HLOptimizerPro.App.Services;

/// <summary>Contrato de navegação por ViewModels (sem referências diretas a janelas/Views).</summary>
public interface INavigationService
{
    /// <summary>Chave da página atual.</summary>
    string CurrentKey { get; }

    /// <summary>ViewModel da página atual.</summary>
    ViewModelBase? Current { get; }

    /// <summary>Evento disparado depois de cada navegação (o shell troca o conteúdo).</summary>
    event EventHandler<ViewModelBase>? Navigated;

    /// <summary>Navega para uma página registada.</summary>
    Task NavigateToAsync(string key, object? parameter = null);

    /// <summary>Volta à página anterior (histórico).</summary>
    Task GoBackAsync();

    bool CanGoBack { get; }
}

/// <summary>
/// Navegação simples e determinística: um registo de chave → fábrica de ViewModel.
///
/// Porque funciona assim? Porque evita "navegação por strings mágicas" com reflexão e permite
/// que cada ViewModel seja criado pela injeção de dependências (com os serviços que precisa).
/// O histórico suporta voltar atrás (usado pelo botão Voltar da barra de título).
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly Dictionary<string, Func<ViewModelBase>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<string> _history = new();

    public string CurrentKey { get; private set; } = string.Empty;

    public ViewModelBase? Current { get; private set; }

    public event EventHandler<ViewModelBase>? Navigated;

    public bool CanGoBack => _history.Count > 0;

    /// <summary>Regista uma página (chamado no arranque pelo <c>ServiceCollectionExtensions</c>).</summary>
    public void Register(string key, Func<ViewModelBase> factory) => _factories[key] = factory;

    public async Task NavigateToAsync(string key, object? parameter = null)
    {
        if (!_factories.TryGetValue(key, out var factory))
            throw new InvalidOperationException($"Página não registada: {key}");

        // Não empilha a página atual quando o utilizador clica no mesmo item da sidebar.
        if (!string.IsNullOrEmpty(CurrentKey) && !string.Equals(CurrentKey, key, StringComparison.OrdinalIgnoreCase))
            _history.Push(CurrentKey);

        // Avisa a página anterior de que saiu (pode parar timers).
        if (Current is { } previous)
            await previous.OnNavigatedFromAsync().ConfigureAwait(true);

        var viewModel = factory();
        Current = viewModel;
        CurrentKey = key;

        await viewModel.OnNavigatedToAsync(parameter).ConfigureAwait(true);
        Navigated?.Invoke(this, viewModel);
    }

    public async Task GoBackAsync()
    {
        if (_history.Count == 0) return;

        var key = _history.Pop();

        if (Current is { } previous)
            await previous.OnNavigatedFromAsync().ConfigureAwait(true);

        if (!_factories.TryGetValue(key, out var factory)) return;
        var viewModel = factory();
        Current = viewModel;
        CurrentKey = key;

        await viewModel.OnNavigatedToAsync(null).ConfigureAwait(true);
        Navigated?.Invoke(this, viewModel);
    }

    /// <summary>Limpa o histórico (usado no logout).</summary>
    public void ClearHistory() => _history.Clear();
}

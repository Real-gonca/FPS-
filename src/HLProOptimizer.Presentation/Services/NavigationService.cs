using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.Services;

/// <summary>
/// Navegação entre as telas da área de conteúdo da <c>MainWindow</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registro por chave.</b> Cada tela é registrada com uma chave estável
/// (<see cref="NavigationKeys"/>) associada ao tipo do ViewModel. Assim os
/// ViewModels navegam entre si sem se conhecerem (nenhuma referência direta).
/// </para>
/// <para>
/// <b>ViewModels singleton.</b> Resolvidos uma vez pelo container e reutilizados:
/// o estado da tela (gráfico do monitoramento, resultados do scan) sobrevive à
/// troca de abas, e <see cref="INavigableViewModel"/> controla o que precisa ser
/// recarregado ao entrar/sair.
/// </para>
/// </remarks>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _provider;
    private readonly ILogger<NavigationService> _logger;
    private readonly Dictionary<string, Type> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _backStack = [];

    /// <summary>Cria o serviço de navegação.</summary>
    /// <param name="provider">Container de DI.</param>
    /// <param name="logger">Logger.</param>
    public NavigationService(IServiceProvider provider, ILogger<NavigationService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    /// <inheritdoc />
    public object? CurrentViewModel { get; private set; }

    /// <inheritdoc />
    public string? CurrentKey { get; private set; }

    /// <inheritdoc />
    public bool CanGoBack => _backStack.Count > 0;

    /// <inheritdoc />
    public event EventHandler<NavigationEventArgs>? Navigated;

    /// <summary>Registra uma tela (chave → tipo do ViewModel).</summary>
    /// <typeparam name="TViewModel">Tipo do ViewModel.</typeparam>
    /// <param name="navigationKey">Chave de navegação.</param>
    /// <returns>O próprio serviço (fluent).</returns>
    public NavigationService Register<TViewModel>(string navigationKey)
        where TViewModel : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationKey);

        _registrations[navigationKey] = typeof(TViewModel);

        return this;
    }

    /// <summary>Registra várias telas de uma vez.</summary>
    /// <param name="registrations">Pares (chave, tipo do ViewModel).</param>
    public void RegisterRange(IEnumerable<KeyValuePair<string, Type>> registrations)
    {
        foreach (var registration in registrations)
        {
            _registrations[registration.Key] = registration.Value;
        }
    }

    /// <summary>Chaves registradas (usadas pelo menu lateral).</summary>
    public IReadOnlyCollection<string> RegisteredKeys => _registrations.Keys;

    /// <inheritdoc />
    public bool NavigateTo(string navigationKey, object? parameter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationKey);

        if (string.Equals(navigationKey, CurrentKey, StringComparison.OrdinalIgnoreCase))
        {
            // Já está na tela: apenas repassa o parâmetro (ex.: abrir um item específico).
            (CurrentViewModel as INavigableViewModel)?.OnNavigatedTo(parameter);

            return true;
        }

        if (!_registrations.TryGetValue(navigationKey, out var viewModelType))
        {
            _logger.LogWarning("Chave de navegação não registrada: {Key}.", navigationKey);
            return false;
        }

        try
        {
            var viewModel = ResolveViewModel(navigationKey, viewModelType);

            // Sai da tela anterior (para timers/eventos).
            (CurrentViewModel as INavigableViewModel)?.OnNavigatedFrom();

            if (!string.IsNullOrEmpty(CurrentKey))
            {
                _backStack.Add(CurrentKey);

                // Histórico limitado: evita crescimento infinito em sessões longas.
                if (_backStack.Count > 20)
                {
                    _backStack.RemoveAt(0);
                }
            }

            var previousKey = CurrentKey;

            CurrentKey = navigationKey;
            CurrentViewModel = viewModel;

            (viewModel as INavigableViewModel)?.OnNavigatedTo(parameter);

            Navigated?.Invoke(this, new NavigationEventArgs(previousKey, navigationKey, viewModel));

            _logger.LogDebug("Navegação: {Previous} → {New}.", previousKey ?? "(início)", navigationKey);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao navegar para {Key}.", navigationKey);
            return false;
        }
    }

    /// <inheritdoc />
    public void GoBack()
    {
        if (_backStack.Count == 0)
        {
            return;
        }

        var previous = _backStack[^1];

        _backStack.RemoveAt(_backStack.Count - 1);

        // NavigateTo empilharia a tela atual; removemos o efeito colateral logo em seguida.
        var stackSnapshot = _backStack.ToList();

        if (NavigateTo(previous))
        {
            _backStack.Clear();

            foreach (var key in stackSnapshot)
            {
                _backStack.Add(key);
            }
        }
    }

    /// <summary>Resolve (e guarda em cache) o ViewModel da tela.</summary>
    private object ResolveViewModel(string navigationKey, Type viewModelType)
    {
        if (_instances.TryGetValue(navigationKey, out var cached))
        {
            return cached;
        }

        // ActivatorUtilities permite construtores com parâmetros não registrados.
        var instance = _provider.GetService(viewModelType)
            ?? ActivatorUtilities.CreateInstance(_provider, viewModelType);

        _instances[navigationKey] = instance;

        return instance;
    }
}

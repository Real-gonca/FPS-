using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// Contrato dos ViewModels que participam da navegação (ciclo de vida da tela).
/// </summary>
/// <remarks>
/// Os ViewModels são singletons no container: <see cref="OnNavigatedTo"/> é o ponto
/// certo para refrescar dados ao entrar na tela (e <see cref="OnNavigatedFrom"/>
/// para parar timers, como no Monitoramento).
/// </remarks>
public interface INavigableViewModel
{
    /// <summary>Chamado quando a tela se torna visível.</summary>
    /// <param name="parameter">Parâmetro de navegação (opcional).</param>
    void OnNavigatedTo(object? parameter);

    /// <summary>Chamado quando a tela sai de foco.</summary>
    void OnNavigatedFrom();
}

/// <summary>
/// Base dos ViewModels: estado ocupado, mensagens e acesso à localização.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread affinity.</b> Coleções observáveis só podem ser mutadas na thread de UI.
/// <see cref="OnUiThreadAsync"/> garante isso para trabalho vindo de background
/// (ex.: amostras do monitor, eventos de serviços).
/// </para>
/// <para>
/// <b>Erros nunca escapam.</b> <see cref="RunBusyAsync"/> captura e registra qualquer
/// exceção, devolvendo uma mensagem amigável via <see cref="StatusMessage"/> — um
/// ViewModel jamais derruba o aplicativo por uma falha de WMI/registro.
/// </para>
/// </remarks>
public abstract partial class ViewModelBase : ObservableObject, INavigableViewModel
{
    private readonly ILogger _logger;

    /// <summary>Cria a base do ViewModel.</summary>
    /// <param name="localization">Serviço de localização.</param>
    /// <param name="logger">Logger.</param>
    protected ViewModelBase(ILocalizationService localization, ILogger logger)
    {
        Localization = localization;
        _logger = logger;
    }

    /// <summary>Serviço de localização (textos em PT-BR/EN/ES).</summary>
    protected ILocalizationService Localization { get; }

    /// <summary>Indica se há uma operação longa em andamento.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Mensagem exibida enquanto <see cref="IsBusy"/> está ativo.</summary>
    [ObservableProperty]
    private string? _busyMessage;

    /// <summary>Mensagem de status/erro da última operação (barra inferior da tela).</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Indica se a última operação terminou com erro.</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>Texto localizado pela chave.</summary>
    /// <param name="key">Chave do recurso.</param>
    protected string L(string key) => Localization[key];

    /// <summary>Texto localizado formatado.</summary>
    /// <param name="key">Chave do recurso.</param>
    /// <param name="args">Argumentos.</param>
    protected string LF(string key, params object?[] args) => Localization.GetString(key, args);

    /// <inheritdoc />
    public virtual void OnNavigatedTo(object? parameter)
    {
    }

    /// <inheritdoc />
    public virtual void OnNavigatedFrom()
    {
    }

    /// <summary>
    /// Executa um trabalho assíncrono com <see cref="IsBusy"/>/mensagem e tratamento de erro.
    /// </summary>
    /// <param name="work">Trabalho a executar.</param>
    /// <param name="busyMessage">Mensagem exibida durante a execução.</param>
    /// <param name="successMessage">Mensagem de sucesso (opcional).</param>
    protected async Task RunBusyAsync(Func<Task> work, string busyMessage, string? successMessage = null)
    {
        if (IsBusy)
        {
            StatusMessage = L("Msg_ScanInProgress");
            return;
        }

        IsBusy = true;
        HasError = false;
        BusyMessage = busyMessage;
        StatusMessage = busyMessage;

        try
        {
            await work().ConfigureAwait(true);

            StatusMessage = successMessage ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L("Dlg_CancelledTitle");
            _logger.LogInformation("Operação cancelada pelo usuário em {ViewModel}.", GetType().Name);
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            _logger.LogError(ex, "Falha em operação de {ViewModel}.", GetType().Name);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    /// <summary>Executa uma ação na thread de UI (necessário para ObservableCollection).</summary>
    /// <param name="action">Ação.</param>
    protected static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    /// <summary>Versão assíncrona de <see cref="OnUiThread"/>.</summary>
    /// <param name="action">Ação.</param>
    protected static Task OnUiThreadAsync(Func<Task> action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        return dispatcher.InvokeAsync(action).Task.Unwrap();
    }
}

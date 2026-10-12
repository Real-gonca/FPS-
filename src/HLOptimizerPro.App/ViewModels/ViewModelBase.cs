using CommunityToolkit.Mvvm.ComponentModel;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>
/// Base de todos os ViewModels.
///
/// Responsabilidades comuns (evita duplicação em 15 ViewModels):
///   • título/subtítulo/ícone da página (usados pelo cabeçalho do shell);
///   • estado de ocupação (loading) ligado ao indicador de progresso;
///   • ciclo de vida de navegação (arranca/para timers quando a página entra/sai);
///   • mensagens de estado (sucesso/erro) apresentadas na própria página;
///   • acesso ao serviço de notificações para feedback visual consistente.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    protected ViewModelBase(NotificationService notifications)
    {
        Notifications = notifications;
    }

    /// <summary>Notificações da aplicação (toasts no canto superior direito).</summary>
    protected NotificationService Notifications { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>Nome do ícone (Material Design Icons) apresentado junto ao título.</summary>
    [ObservableProperty]
    private string _iconKind = "ViewDashboard";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = "A processar…";

    /// <summary>Mensagem de estado local à página (ex.: "12 ficheiros removidos").</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>True quando a última operação falhou (colore a mensagem de estado a vermelho).</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>Indica se a página está visível (as páginas param os seus timers quando saem).</summary>
    public bool IsActive { get; private set; }

    /// <summary>Chamado quando a página passa a estar visível.</summary>
    public virtual Task OnNavigatedToAsync(object? parameter = null)
    {
        IsActive = true;
        return Task.CompletedTask;
    }

    /// <summary>Chamado quando a página deixa de estar visível.</summary>
    public virtual Task OnNavigatedFromAsync()
    {
        IsActive = false;
        return Task.CompletedTask;
    }

    /// <summary>Atualiza a mensagem de estado e notifica (feedback visual uniforme).</summary>
    protected void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        HasError = isError;
    }

    /// <summary>Mostra o resultado de uma operação: notificação + mensagem de estado.</summary>
    protected void Report(Result result, string? actionTitle = null)
    {
        SetStatus(result.Message, !result.Success);

        if (result.Success)
            Notifications.Success(actionTitle ?? Title, result.Message);
        else
            Notifications.Error(actionTitle ?? Title, result.Message);
    }

    /// <summary>Executa uma operação assíncrona com indicador de ocupação e tratamento de erros.</summary>
    protected async Task RunBusyAsync(Func<Task> operation, string busyMessage = "A processar…")
    {
        if (IsBusy) return;

        IsBusy = true;
        BusyMessage = busyMessage;

        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Operação cancelada pelo utilizador.");
        }
        catch (Exception ex)
        {
            SetStatus($"Ocorreu um erro: {ex.Message}", isError: true);
            Notifications.Error(Title, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

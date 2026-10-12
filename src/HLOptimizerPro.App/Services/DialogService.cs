using System.Windows;
using HLOptimizerPro.App.Views.Dialogs;
using HLOptimizerPro.Core.Models;
using Microsoft.Win32;

namespace HLOptimizerPro.App.Services;

/// <summary>Contrato de diálogos da aplicação (implementação trocável em testes).</summary>
public interface IDialogService
{
    /// <summary>Pedido de confirmação (usado antes de qualquer ação de risco).</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continuar", bool danger = false);

    /// <summary>Diálogo informativo.</summary>
    Task ShowInfoAsync(string title, string message);

    /// <summary>Apresenta o modo Simular: lista do que seria alterado.</summary>
    Task ShowSimulationAsync(string title, IReadOnlyList<string> changes);

    string? PickFile(string filter, string title = "Selecionar ficheiro");

    string? PickSaveFile(string filter, string defaultFileName, string title = "Guardar como");

    string? PickFolder(string title = "Selecionar pasta");

    /// <summary>Abre um ficheiro/pasta com a aplicação predefinida do Windows.</summary>
    void OpenInShell(string path);
}

/// <summary>
/// Implementação dos diálogos com a identidade visual da aplicação (janela própria em vez das
/// MessageBox cinzentas do Windows). Mantém o comportamento previsível: sempre modal, sempre
/// centrado na janela principal.
/// </summary>
public sealed class DialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continuar", bool danger = false)
        => ShowDialogAsync(DialogWindow.CreateConfirm(title, message, confirmText, danger));

    public Task ShowInfoAsync(string title, string message)
        => ShowDialogAsync(DialogWindow.CreateInfo(title, message));

    public Task ShowSimulationAsync(string title, IReadOnlyList<string> changes)
        => ShowDialogAsync(DialogWindow.CreateSimulation(title, changes));

    public string? PickFile(string filter, string title = "Selecionar ficheiro")
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string filter, string defaultFileName, string title = "Guardar como")
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName,
            OverwritePrompt = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title = "Selecionar pasta")
    {
        // O OpenFolderDialog existe no .NET 8 (Microsoft.Win32) — sem COM nem interop extra.
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public void OpenInShell(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _ = ShowInfoAsync("Não foi possível abrir", $"{path}\n\n{ex.Message}");
        }
    }

    private static Task<bool> ShowDialogAsync(DialogWindow dialog)
    {
        var owner = Application.Current?.MainWindow;

        if (owner is not null && owner.IsLoaded && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return Task.FromResult(dialog.ShowDialog() == true);
    }
}

/// <summary>
/// Converte um <see cref="Result"/> num diálogo/notificação consistente.
/// Centralizar isto evita que cada ViewModel invente o seu formato de mensagem.
/// </summary>
public sealed class UserFeedbackService
{
    private readonly IDialogService _dialogs;
    private readonly Core.Services.NotificationService _notifications;

    public UserFeedbackService(IDialogService dialogs, Core.Services.NotificationService notifications)
    {
        _dialogs = dialogs;
        _notifications = notifications;
    }

    /// <summary>Mostra o resultado: notificação em caso de sucesso, diálogo em caso de falha.</summary>
    public async Task ReportAsync(string title, Result result)
    {
        if (result.Success)
        {
            _notifications.Success(title, result.Message);
        }
        else
        {
            _notifications.Error(title, result.Message);
            await _dialogs.ShowInfoAsync(title, result.Message);
        }
    }

    /// <summary>Pede confirmação antes de uma operação de risco, explicando a reversão.</summary>
    public Task<bool> ConfirmRiskyAsync(string title, string message, string confirmText = "Aplicar mesmo assim")
        => _dialogs.ConfirmAsync(title, message, confirmText, danger: true);
}

using System.Windows;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Presentation.Views.Dialogs;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.Services;

/// <summary>
/// Implementação WPF de <see cref="IDialogService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que uma abstração?</b> Os ViewModels vivem fora do WPF conceitualmente
/// (podem ser testados sem STA/UI). Todo diálogo passa por aqui, e o
/// <see cref="Marshalled{T}"/> garante a thread de UI mesmo quando a chamada vem de
/// um <c>Task.Run</c> ou de um evento de background.
/// </para>
/// <para>
/// <b>Elevação sob demanda</b>: quando o usuário clica em "Reexecutar como
/// administrador" no diálogo de resultado, o serviço dispara
/// <see cref="RetryElevatedRequested"/> — quem decide reiniciar o processo é a
/// composição raiz (<c>App.xaml.cs</c>), não o diálogo.
/// </para>
/// </remarks>
public sealed class DialogService : IDialogService
{
    private readonly ILocalizationService _localization;
    private readonly ILogger<DialogService> _logger;

    /// <summary>Cria o serviço de diálogos.</summary>
    /// <param name="localization">Localização (títulos padrão).</param>
    /// <param name="logger">Logger.</param>
    public DialogService(ILocalizationService localization, ILogger<DialogService> logger)
    {
        _localization = localization;
        _logger = logger;
    }

    /// <summary>Disparado quando o usuário pede a reexecução elevada de uma otimização.</summary>
    public event EventHandler? RetryElevatedRequested;

    /// <inheritdoc />
    public Task ShowInfoAsync(string title, string message) =>
        ShowMessageAsync(title, message, MessageDialogKind.Info);

    /// <inheritdoc />
    public Task ShowWarningAsync(string title, string message) =>
        ShowMessageAsync(title, message, MessageDialogKind.Warning);

    /// <inheritdoc />
    public Task ShowErrorAsync(string title, string message) =>
        ShowMessageAsync(title, message, MessageDialogKind.Error);

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Confirmar",
        string cancelText = "Cancelar",
        bool isDestructive = false)
    {
        return Marshalled(() =>
        {
            _logger.LogDebug("Confirmação solicitada: {Title} (destrutiva={Destructive}).", title, isDestructive);

            return MessageDialog.Show(
                FindOwner(),
                title,
                message,
                isDestructive ? MessageDialogKind.Warning : MessageDialogKind.Question,
                isConfirmation: true,
                confirmText: confirmText,
                cancelText: cancelText,
                isDestructive: isDestructive);
        });
    }

    /// <inheritdoc />
    public Task<bool> ShowProgressAsync(string title, Func<IProgress<ScanProgress>, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return Marshalled(() =>
        {
            var dialog = new ProgressDialog(title, work);
            var owner = FindOwner();

            if (owner is not null)
            {
                dialog.Owner = owner;
            }

            // ShowDialog bomba um loop de mensagens aninhado: o trabalho assíncrono
            // continua progredindo enquanto a janela está modal.
            dialog.ShowDialog();

            if (dialog.WasCancelled)
            {
                _logger.LogInformation("Operação '{Title}' cancelada pelo usuário.", title);
            }

            return dialog.WasCompleted;
        });
    }

    /// <inheritdoc />
    public Task<string?> ShowOpenFileDialogAsync(FileDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Marshalled<string?>(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = options.Title,
                Filter = string.IsNullOrWhiteSpace(options.Filter) ? "Todos os arquivos|*.*" : options.Filter,
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (!string.IsNullOrWhiteSpace(options.InitialDirectory) && Directory.Exists(options.InitialDirectory))
            {
                dialog.InitialDirectory = options.InitialDirectory;
            }

            if (!string.IsNullOrWhiteSpace(options.InitialFileName))
            {
                dialog.FileName = options.InitialFileName;
            }

            return dialog.ShowDialog(FindOwner()) == true ? dialog.FileName : null;
        });
    }

    /// <inheritdoc />
    public Task<string?> ShowSaveFileDialogAsync(FileDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Marshalled<string?>(() =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = options.Title,
                Filter = string.IsNullOrWhiteSpace(options.Filter) ? "Todos os arquivos|*.*" : options.Filter,
                OverwritePrompt = true,
                AddExtension = true
            };

            if (!string.IsNullOrWhiteSpace(options.InitialDirectory) && Directory.Exists(options.InitialDirectory))
            {
                dialog.InitialDirectory = options.InitialDirectory;
            }

            if (!string.IsNullOrWhiteSpace(options.InitialFileName))
            {
                dialog.FileName = options.InitialFileName;
            }

            if (!string.IsNullOrWhiteSpace(options.DefaultExtension))
            {
                dialog.DefaultExt = options.DefaultExtension;
            }

            return dialog.ShowDialog(FindOwner()) == true ? dialog.FileName : null;
        });
    }

    /// <inheritdoc />
    public Task ShowOptimizationResultAsync(OptimizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return Marshalled(() =>
        {
            var dialog = new OptimizationResultDialog(result, _localization);
            var owner = FindOwner();

            if (owner is not null)
            {
                dialog.Owner = owner;
            }

            dialog.ShowDialog();

            if (dialog.RetryElevated)
            {
                _logger.LogInformation("Usuário solicitou a reexecução elevada da otimização.");

                RetryElevatedRequested?.Invoke(this, EventArgs.Empty);
            }

            return true;
        });
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    private Task ShowMessageAsync(string title, string message, MessageDialogKind kind) =>
        Marshalled(() =>
        {
            var effectiveTitle = string.IsNullOrWhiteSpace(title) ? DefaultTitleFor(kind) : title;

            _logger.LogDebug("Diálogo {Kind}: {Title}", kind, effectiveTitle);

            return MessageDialog.Show(FindOwner(), effectiveTitle, message, kind);
        });

    private string DefaultTitleFor(MessageDialogKind kind) => kind switch
    {
        MessageDialogKind.Error => _localization["Dlg_ErrorTitle"],
        MessageDialogKind.Warning => _localization["Dlg_WarningTitle"],
        MessageDialogKind.Success => _localization["Dlg_SuccessTitle"],
        MessageDialogKind.Question => _localization["Dlg_ElevationTitle"],
        _ => _localization["App_Title"]
    };

    /// <summary>Janela dona dos diálogos (a ativa, ou a principal).</summary>
    private static Window? FindOwner()
    {
        var application = Application.Current;

        if (application is null)
        {
            return null;
        }

        var active = application.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive && w.IsLoaded);

        return active ?? application.MainWindow;
    }

    /// <summary>Executa uma função na thread de UI e devolve o resultado como Task.</summary>
    private Task<T> Marshalled<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            try
            {
                return Task.FromResult(action());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao exibir um diálogo.");
                return Task.FromResult(default(T)!);
            }
        }

        return dispatcher.InvokeAsync(() =>
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao exibir um diálogo na thread de UI.");
                return default(T)!;
            }
        }).Task;
    }
}

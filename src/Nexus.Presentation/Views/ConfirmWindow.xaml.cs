using System.Windows;

namespace Nexus.Presentation.Views;

/// <summary>
/// Diálogo de confirmação com tema dark-neon (spec §3) — usado para o
/// "preview de impacto antes de aplicar" (serviços/perfis) e para ações
/// destrutivas. Modal ao owner, Topmost, sem taskbar.
/// </summary>
public partial class ConfirmWindow : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmWindow()
    {
        InitializeComponent();

        OkButton.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };
        CancelButton.Click += (_, _) =>
        {
            Confirmed = false;
            Close();
        };
    }

    /// <summary>Ajuda assíncrona: true se o utilizador confirmar.</summary>
    public static Task<bool> ShowAsync(
        Window? owner,
        string title,
        string message,
        string? details = null,
        string okText = "Confirmar")
    {
        var window = new ConfirmWindow
        {
            Owner = owner,
            Title = title,
            Confirmed = false,
        };
        window.HeaderText.Text = title;
        window.MessageText.Text = message;
        window.OkButton.Content = okText;

        if (!string.IsNullOrWhiteSpace(details))
        {
            window.DetailsBox.Visibility = Visibility.Visible;
            window.DetailsText.Text = details;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => tcs.TrySetResult(window.Confirmed);
        window.Show();
        return tcs.Task;
    }
}

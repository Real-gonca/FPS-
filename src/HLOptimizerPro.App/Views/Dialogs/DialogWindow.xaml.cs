using System.Windows;
using System.Windows.Input;

namespace HLOptimizerPro.App.Views.Dialogs;

/// <summary>
/// Diálogo modal da aplicação. As três variantes usadas pelo código são criadas pelas fábricas
/// estáticas <see cref="CreateConfirm"/>, <see cref="CreateInfo"/> e <see cref="CreateSimulation"/>.
/// </summary>
public partial class DialogWindow : Window
{
    private DialogWindow()
    {
        InitializeComponent();

        // ESC fecha; ENTER confirma (comportamento esperado em diálogos).
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && ConfirmButton.IsEnabled)
            {
                DialogResult = true;
                e.Handled = true;
            }
        };
    }

    /// <summary>Confirmação com botão de ação (usado antes de alterações de risco).</summary>
    public static DialogWindow CreateConfirm(string title, string message, string confirmText, bool danger)
    {
        var window = new DialogWindow();
        window.TitleText.Text = title;
        window.MessageText.Text = message;
        window.ConfirmButton.Content = confirmText;
        window.CancelButton.Visibility = Visibility.Visible;
        window.HeaderIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ShieldAlertOutline;

        if (danger)
        {
            window.ConfirmButton.Style = (Style)window.FindResource("ButtonDanger");
            window.HeaderIcon.Foreground = (System.Windows.Media.Brush)window.FindResource("BrushDanger");
        }

        return window;
    }

    /// <summary>Aviso informativo (um único botão).</summary>
    public static DialogWindow CreateInfo(string title, string message)
    {
        var window = new DialogWindow();
        window.TitleText.Text = title;
        window.MessageText.Text = message;
        window.ConfirmButton.Content = "Entendi";
        window.CancelButton.Visibility = Visibility.Collapsed;
        window.HeaderIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.InformationOutline;

        return window;
    }

    /// <summary>Modo Simular: mensagem + lista detalhada do que seria alterado.</summary>
    public static DialogWindow CreateSimulation(string title, IReadOnlyList<string> changes)
    {
        var window = new DialogWindow();
        window.TitleText.Text = title;
        window.MessageText.Text =
            "Modo Simular ativo: nada foi alterado no seu sistema. Abaixo está exatamente o que " +
            "seria modificado se desativar o modo Simular.";
        window.ConfirmButton.Content = "Fechar";
        window.CancelButton.Visibility = Visibility.Collapsed;
        window.HeaderIcon.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.EyeOutline;

        window.ChangesPanel.Visibility = Visibility.Visible;
        window.ChangesList.ItemsSource = changes;

        return window;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}

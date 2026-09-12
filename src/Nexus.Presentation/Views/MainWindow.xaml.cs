using System.Windows;
using System.Windows.Input;
using Nexus.Presentation.Notifications;
using Nexus.Presentation.ViewModels;

namespace Nexus.Presentation.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // ── Botões da title bar (WindowChrome) ─────────────────────────────
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    // ── Seletor de modo Simples/Avançado ───────────────────────────────
    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is string tag &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.IsAdvanced = tag == "advanced";
        }
    }

    // ── Dispensa manual de notificação ─────────────────────────────────
    private void NotificationDismiss_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is NotificationItem item &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.Notifications.Items.Remove(item);
        }
    }
}

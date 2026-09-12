using System.Windows;
using System.Windows.Controls;
using Nexus.Presentation.ViewModels;

namespace Nexus.Presentation.Views;

public partial class ServicesView : UserControl
{
    public ServicesView()
    {
        InitializeComponent();
    }

    private async void ProfileApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ProfileRow row)
            await row.ApplyAsync();
    }
}

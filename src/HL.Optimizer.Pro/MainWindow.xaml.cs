using System.Windows;
using System.Windows.Input;
using HL.Optimizer.Pro.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HL.Optimizer.Pro;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();

        // Enable dragging from header
        MouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && e.GetPosition(this).Y < 64)
                DragMove();
        };
    }
}

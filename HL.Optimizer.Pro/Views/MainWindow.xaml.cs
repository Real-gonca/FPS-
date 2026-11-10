using System.Windows;
using HL.Optimizer.ViewModels;
namespace HL.Optimizer.Views;
public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    public MainWindow()
    {
        InitializeComponent();
        model = new MainViewModel(text => MessageBox.Show(this, text, "HL · Confirm operation", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        DataContext = model;
        Loaded += (_, _) => model.Refresh.Execute(null);
        Closed += (_, _) => model.Dispose();
    }
}

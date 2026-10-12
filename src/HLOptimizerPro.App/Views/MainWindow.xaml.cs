using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HLOptimizerPro.App.Views;

/// <summary>
/// Janela principal (shell).
///
/// Aqui vive apenas código de apresentação: criar a View correspondente a cada ViewModel
/// (ver <see cref="ViewLocator"/>), manter o ícone de maximizar/restaurar coerente e reproduzir
/// a animação de entrada. Toda a lógica está nos ViewModels.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = App.Container?.GetRequiredService<MainViewModel>() ?? throw new InvalidOperationException(
            "Contentor de serviços indisponível.");

        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        StateChanged += (_, _) => UpdateMaximizeIcon();
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.StopTimers();
        };

        Loaded += async (_, _) => await _viewModel.OnNavigatedToAsync();
    }

    /// <summary>Troca a View no anfitrião sempre que o ViewModel da página muda.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CurrentPage)) return;

        PageHost.Content = ViewLocator.Resolve(_viewModel.CurrentPage);
    }

    /// <summary>Animação suave de entrada da janela (opcional, pedida no arranque).</summary>
    public void PlayIntroAnimation()
    {
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        BeginAnimation(OpacityProperty, animation);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
            app.RequestShutdown();
    }

    /// <summary>Mantém o ícone do botão coerente com o estado da janela.</summary>
    private void UpdateMaximizeIcon()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Kind = maximized
            ? MahApps.Metro.IconPacks.PackIconMaterialKind.WindowRestore
            : MahApps.Metro.IconPacks.PackIconMaterialKind.WindowMaximize;

        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
    }

    /// <summary>Duplo-clique na barra de título maximiza/restaura (como o Windows).</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && e.GetPosition(this).Y <= 46)
            OnMaximizeClick(this, new RoutedEventArgs());

        base.OnMouseLeftButtonDown(e);
    }
}

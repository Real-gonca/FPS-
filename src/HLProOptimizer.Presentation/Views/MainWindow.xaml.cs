using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Presentation.Services;
using HLProOptimizer.Presentation.ViewModels;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.Views;

/// <summary>
/// Janela principal (shell) do HL PRO OPTIMIZER.
/// </summary>
/// <remarks>
/// O code-behind contém apenas comportamento de janela — arrastar, minimizar,
/// fechar para a bandeja e ligar os eventos do <see cref="TrayIconService"/> ao
/// <see cref="MainViewModel"/>. Nenhuma regra de negócio vive aqui (MVVM).
/// </remarks>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISettingsService _settings;
    private readonly ILogger<MainWindow> _logger;

    private bool _isExiting;

    /// <summary>Cria a janela principal.</summary>
    /// <param name="viewModel">ViewModel da shell.</param>
    /// <param name="tray">Serviço do ícone de bandeja.</param>
    /// <param name="settings">Configurações (minimizar para a bandeja).</param>
    /// <param name="navigation">Navegação entre telas.</param>
    /// <param name="logger">Logger.</param>
    public MainWindow(
        MainViewModel viewModel,
        TrayIconService tray,
        ISettingsService settings,
        INavigationService navigation,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _settings = settings;
        _logger = logger;

        DataContext = viewModel;

        InitializeTray(tray, navigation);

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    /// <summary>Inicia na bandeja (quando configurado) sem mostrar a janela.</summary>
    public bool StartHidden { get; set; }

    private void InitializeTray(TrayIconService tray, INavigationService navigation)
    {
        tray.Initialize(this);

        tray.OpenRequested += (_, navigationKey) => Dispatcher.Invoke(() =>
        {
            Show();

            if (!string.IsNullOrWhiteSpace(navigationKey))
            {
                navigation.NavigateTo(navigationKey);
            }
        });

        tray.GameModeToggleRequested += (_, _) => Dispatcher.Invoke(() =>
        {
            Show();
            Activate();

            if (_viewModel.ToggleGameModeCommand.CanExecute(null))
            {
                _viewModel.ToggleGameModeCommand.Execute(null);
            }
        });

        tray.ExitRequested += (_, _) => ExitApplication();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        try
        {
            await _viewModel.LoadSummaryCommand.ExecuteAsync(null);

            _viewModel.NavigateCommand.Execute(NavigationKeys.Dashboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na inicialização da janela principal.");
        }
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        // Duplo clique na barra maximiza/restaura (comportamento nativo esperado).
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "DragMove ignorado.");
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (_settings.Current.MinimizeToTray && _settings.Current.ShowTrayIcon)
        {
            Hide();
            return;
        }

        ExitApplication();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        if (_settings.Current.MinimizeToTray && _settings.Current.ShowTrayIcon)
        {
            e.Cancel = true;
            Hide();
        }
    }

    /// <summary>Encerra o aplicativo de fato (usado pela bandeja e pelo botão fechar).</summary>
    private void ExitApplication()
    {
        _isExiting = true;

        Application.Current.Shutdown();
    }
}

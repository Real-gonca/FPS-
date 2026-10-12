using System.Windows;
using System.Windows.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HLOptimizerPro.App.Views;

/// <summary>
/// Janela de boas-vindas / login.
///
/// O ViewModel é resolvido pelo contentor de injeção de dependências. Aqui fica apenas o que é
/// específico de WPF: arrastar a janela pela barra de título personalizada, sincronizar a
/// PasswordBox com o ViewModel (o controlo não permite binding direto por segurança) e reagir
/// ao evento "sessão iniciada" abrindo a janela principal.
/// </summary>
public partial class WelcomeWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public WelcomeWindow()
    {
        InitializeComponent();

        _viewModel = App.Container?.GetRequiredService<LoginViewModel>() ?? throw new InvalidOperationException(
            "Contentor de serviços indisponível.");

        DataContext = _viewModel;
        _viewModel.SignedIn += OnSignedIn;

        Loaded += (_, _) => UserTextBox.Focus();
        Closed += (_, _) => _viewModel.SignedIn -= OnSignedIn;
    }

    private void OnSignedIn(object? sender, EventArgs e)
    {
        if (Application.Current is App app)
            app.StartMainWindow(skipIntro: false);
    }

    /// <summary>Mantém o ViewModel sincronizado com a PasswordBox.</summary>
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox box)
            _viewModel.Password = box.Password;

        // Se o utilizador escreve a senha e o campo "password visível" estiver sincronizado,
        // evitamos que fiquem valores diferentes.
        if (PasswordVisible.Text != _viewModel.Password)
            PasswordVisible.Text = _viewModel.Password;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove pode lançar se o botão for libertado durante a chamada — ignorar.
            }
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>Item da lista de benefícios mostrada na tela de boas-vindas.</summary>
public sealed record BenefitItem(string IconKind, string Title, string Description);

/// <summary>
/// ViewModel da tela de boas-vindas / login (3.1).
///
/// Fluxo:
///   • "Entrar" — valida a conta local (PBKDF2) e abre o painel;
///   • "Entrar com a Microsoft" — login opcional por MSAL (com aviso claro se não estiver configurado);
///   • "Criar conta" — cria a conta local;
///   • "Agora não" — sessão local sem login (todas as funcionalidades continuam disponíveis).
///
/// A verificação de licença é feita aqui apenas para exibição do estado (Free/Premium); nenhuma
/// funcionalidade é bloqueada no arranque — o utilizador decide depois, no ecrã Sobre/Premium.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly AccountService _accounts;
    private readonly LicenseService _license;
    private readonly MicrosoftAuthService _microsoft;
    private readonly NotificationService _notifications;

    public LoginViewModel(
        AccountService accounts,
        LicenseService license,
        MicrosoftAuthService microsoft,
        NotificationService notifications)
    {
        _accounts = accounts;
        _license = license;
        _microsoft = microsoft;
        _notifications = notifications;

        UserName = accounts.Current.Email;
        RememberMe = accounts.Current.RememberMe || string.IsNullOrEmpty(accounts.Current.Email);
        LicenseText = _license.Current.IsPremium ? "Premium ativada" : "Versão Free";
    }

    // ---- Estado ---------------------------------------------------------------
    [ObservableProperty]
    private string _userName = string.Empty;

    /// <summary>A senha é escrita a partir do código (PasswordBox não permite binding direto).</summary>
    public string Password { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _rememberMe = true;

    [ObservableProperty]
    private bool _showPassword;

    [ObservableProperty]
    private bool _isCreatingAccount;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = "A iniciar sessão…";

    [ObservableProperty]
    private string _licenseText = "Versão Free";

    public string ProductName => AppConstants.ProductName;
    public string Version => AppConstants.Version;
    public string Slogan => AppConstants.Slogan;

    /// <summary>Benefícios listados na coluna esquerda (requisito 3.1).</summary>
    public IReadOnlyList<BenefitItem> Benefits { get; } = new List<BenefitItem>
    {
        new("RocketLaunch", "Melhorar o desempenho", "Otimizações reais e reversíveis no Windows, no arranque e nos serviços."),
        new("Memory", "Reduzir RAM e CPU", "Liberta memória em uso, sem encerrar os seus programas."),
        new("ControllerClassic", "Otimizar jogos", "Modo Jogo, prioridade de processo e latência de entrada reduzida."),
        new("ShieldCheck", "Aumentar a segurança", "Verificações do antivírus, firewall, atualizações e redes."),
        new("Palette", "Personalizar o Windows", "Efeitos visuais, privacidade e comportamento ao seu gosto."),
    };

    /// <summary>Informação de estado da sessão, apresentada no rodapé da janela.</summary>
    public string SessionHint => _license.Current.IsPremium
        ? $"Licença: {_license.Current.TierText}"
        : "Sem licença Premium — funcionalidades avançadas disponíveis em modo de avaliação.";

    // ---- Comandos -------------------------------------------------------------

    /// <summary>Evento pedido ao App para abrir a janela principal (evita acoplar VM → janela).</summary>
    public event EventHandler? SignedIn;

    [RelayCommand]
    private async Task SignInAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(UserName))
        {
            ErrorMessage = "Introduza o seu utilizador ou e-mail.";
            return;
        }

        IsBusy = true;
        BusyMessage = "A iniciar sessão…";

        try
        {
            var result = _accounts.SignIn(UserName, Password, RememberMe);

            if (!result.Success)
            {
                ErrorMessage = result.Message;
                _notifications.Warn("Início de sessão", result.Message);
                return;
            }

            _notifications.Success("Bem-vindo de volta!", result.Message);
            await Task.Delay(180);   // deixa a animação terminar antes de trocar de janela
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAccountAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(UserName) || !UserName.Contains('@'))
        {
            ErrorMessage = "Para criar a conta, introduza um e-mail válido no campo de utilizador.";
            return;
        }

        if (Password.Length < 8)
        {
            ErrorMessage = "Escolha uma senha com pelo menos 8 caracteres.";
            return;
        }

        IsBusy = true;
        BusyMessage = "A criar a conta…";

        try
        {
            var result = _accounts.CreateAccount(UserName.Split('@')[0], UserName, Password);

            if (!result.Success)
            {
                ErrorMessage = result.Message;
                return;
            }

            _notifications.Success("Conta criada", result.Message);
            await Task.Delay(180);
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task MicrosoftSignInAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        BusyMessage = "A contactar a Microsoft…";

        try
        {
            var result = await _microsoft.SignInAsync();

            if (!result.Success)
            {
                ErrorMessage = result.Message;
                return;
            }

            _notifications.Success("Sessão iniciada", result.Message);
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>"Agora não": modo local, sem login (requisito explícito do briefing).</summary>
    [RelayCommand]
    private async Task SkipAsync()
    {
        var result = _accounts.ContinueAsLocal();
        _notifications.Info("Modo local", result.Message);

        await Task.Delay(120);
        SignedIn?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Alterna entre entrar e criar conta.</summary>
    [RelayCommand]
    private void ToggleCreateAccount() => IsCreatingAccount = !IsCreatingAccount;

    [RelayCommand]
    private void ShowForgotPassword()
        => ErrorMessage = "Como não guardamos a sua senha, a recuperação é feita localmente: " +
                          "apague o ficheiro de conta em Configuração e crie uma nova conta. " +
                          "As otimizações aplicadas mantêm-se registadas.";
}

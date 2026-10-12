using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>
/// ViewModel do Sobre / Premium (3.10).
///
/// Mostra a versão, a stack técnica, o estado da licença e os recursos Premium, e permite ativar uma
/// chave (validação offline com assinatura HMAC + cache de 30 dias). Inclui também os termos de
/// utilização e a política de privacidade em texto claro — sem letras pequenas.
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    private readonly LicenseService _license;
    private readonly AccountService _accounts;
    private readonly SystemInfoService _systemInfo;
    private readonly IDialogService _dialogs;

    public AboutViewModel(
        NotificationService notifications,
        LicenseService license,
        AccountService accounts,
        SystemInfoService systemInfo,
        IDialogService dialogs)
        : base(notifications)
    {
        _license = license;
        _accounts = accounts;
        _systemInfo = systemInfo;
        _dialogs = dialogs;

        Title = "Sobre / Premium";
        Subtitle = "Versão, licença, termos e privacidade";
        IconKind = "Information";

        LicenseKeyInput = _license.Current.MaskedKey;
        LicensedTo = _license.Current.LicensedTo;

        RefreshLicenseState();
        RefreshPremiumFeatures();
    }

    // ---- Identificação ----------------------------------------------------------

    public string ProductName => AppConstants.ProductName;
    public string Version => AppConstants.Version;
    public string Publisher => AppConstants.Publisher;
    public string Slogan => AppConstants.Slogan;
    public string SupportEmail => AppConstants.SupportEmail;

    /// <summary>Stack técnica (requisito do briefing: mostrar .NET 8 e C#/WPF).</summary>
    public IReadOnlyList<(string Label, string Value)> Stack { get; } = new List<(string, string)>
    {
        ("Plataforma", ".NET 8 (net8.0-windows)"),
        ("Linguagem", "C# 12"),
        ("Interface", "WPF (MVVM com CommunityToolkit.Mvvm)"),
        ("Gráficos", "OxyPlot"),
        ("Ícones", "Material Design (MahApps IconPacks)"),
        ("Monitorização", "PerformanceCounter + WMI + LibreHardwareMonitor"),
        ("Persistência", "JSON em %AppData%\\HLOptimizerPro"),
        ("Registos", "Serilog (ficheiros com retenção de 30 dias)"),
        ("Arquitetura", "x64, execução como administrador (manifesto UAC)"),
    };

    // ---- Licença -----------------------------------------------------------------

    [ObservableProperty] private string _licenseStateText = "Versão Free";
    [ObservableProperty] private string _licenseKeyInput = string.Empty;
    [ObservableProperty] private string _licensedTo = string.Empty;
    [ObservableProperty] private string _licenseExpiryText = string.Empty;
    [ObservableProperty] private string _activationMessage = string.Empty;
    [ObservableProperty] private bool _isPremium;
    [ObservableProperty] private string _accountText = string.Empty;
    [ObservableProperty] private string _activationHint = "Formato: HLPR-XXXX-XXXX-XXXX-CCCC";

    public ObservableCollection<(string Name, string Description)> PremiumFeatures { get; } = new();

    private void RefreshLicenseState()
    {
        IsPremium = _license.Current.IsPremium;
        LicenseStateText = _license.Current.TierText;
        LicenseExpiryText = _license.Current.ExpiryText;
        LicensedTo = _license.Current.LicensedTo;
        AccountText = $"{_accounts.Current.DisplayName} • {_accounts.Current.ProfileText}";
    }

    private void RefreshPremiumFeatures()
    {
        PremiumFeatures.Clear();

        foreach (var feature in LicenseInfo.PremiumFeatures)
        {
            var parts = feature.Split(':', 2);
            PremiumFeatures.Add(parts.Length == 2
                ? (parts[0].Trim(), parts[1].Trim())
                : (feature, string.Empty));
        }
    }

    [RelayCommand]
    private async Task ActivateLicenseAsync()
    {
        var key = LicenseKeyInput?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(key) || key.StartsWith("HLPR-****", StringComparison.OrdinalIgnoreCase))
        {
            await _dialogs.ShowInfoAsync("Chave inválida",
                "Introduza a chave de licença completa que recebeu (formato HLPR-XXXX-XXXX-XXXX-CCCC).\n\n" +
                "Se ainda não tem chave, a versão Free continua totalmente funcional para otimizações essenciais.");
            return;
        }

        var licensedTo = string.IsNullOrWhiteSpace(LicensedTo) ? _accounts.Current.DisplayName : LicensedTo;

        var result = _license.Activate(key, licensedTo);

        Report(result, "Licença");
        ActivationMessage = result.Message;

        if (result.Success)
        {
            // A chave é guardada mascarada — não repetimos o valor completo na interface.
            LicenseKeyInput = _license.Current.MaskedKey;
            RefreshLicenseState();
        }
    }

    [RelayCommand]
    private async Task DeactivateLicenseAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            "Desativar licença",
            "A chave é removida deste computador. Pode voltar a ativá-la mais tarde com a mesma chave.",
            "Desativar");

        if (!confirmed) return;

        var result = _license.Deactivate();
        Report(result, "Licença");

        LicenseKeyInput = string.Empty;
        RefreshLicenseState();
    }

    [RelayCommand]
    private void RevalidateLicense()
    {
        var result = _license.Revalidate();
        Report(result, "Licença");
        RefreshLicenseState();
    }

    [RelayCommand]
    private void CopyMachineInfo()
    {
        try
        {
            var text = $"HL Optimizer Pro {AppConstants.Version}\n" +
                       $"Utilizador: {_accounts.Current.DisplayName} ({_accounts.Current.ProfileText})\n" +
                       $"Licença: {_license.Current.TierText} {_license.Current.ExpiryText}\n" +
                       $"Hardware: {_systemInfo.Hardware.Summary}\n" +
                       $"Identificador da máquina: {_systemInfo.MachineFingerprint}";

            System.Windows.Clipboard.SetText(text);
            SetStatus("Informação copiada para a área de transferência.");
        }
        catch (Exception ex)
        {
            SetStatus($"Não foi possível copiar: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private async Task ShowTermsAsync()
    {
        await _dialogs.ShowInfoAsync("Termos de Utilização", TermsText);
    }

    [RelayCommand]
    private async Task ShowPrivacyAsync()
    {
        await _dialogs.ShowInfoAsync("Política de Privacidade", PrivacyText);
    }

    [RelayCommand]
    private void OpenSupportEmail() => _dialogs.OpenInShell($"mailto:{AppConstants.SupportEmail}");

    /// <summary>Termos resumidos em linguagem clara (o texto completo acompanha o instalador).</summary>
    public string TermsText =>
        "1. O HL Optimizer Pro é uma ferramenta de otimização e monitorização para Windows. " +
        "É fornecido «tal como está», sem garantia de resultados específicos de desempenho.\n\n" +
        "2. As alterações ao sistema são feitas com o seu consentimento explícito e são reversíveis: " +
        "guardamos o valor original e criamos um ponto de restauro antes de lotes de risco.\n\n" +
        "3. Recomendamos que não utilize a aplicação em simultâneo com outros utilitários de otimização " +
        "que alterem as mesmas definições — podem entrar em conflito.\n\n" +
        "4. A licença (Free ou Premium) é pessoal e válida para os computadores indicados na ativação. " +
        "A validação é local, com assinatura HMAC, e funciona offline até 30 dias.\n\n" +
        "5. O fabricante não é responsável por perdas de dados resultantes do encerramento manual de " +
        "programas pelo utilizador (os processos críticos do Windows estão protegidos e nunca são encerrados).";

    /// <summary>Política de privacidade (o que sai — e o que nunca sai — deste computador).</summary>
    public string PrivacyText =>
        "Resumo: por predefinição, nada sai do seu computador.\n\n" +
        "• Telemetria: DESLIGADA por omissão. Se a ativar na Configuração, enviamos apenas contadores anónimos " +
        "de utilização (versão, sistema, número de otimizações aplicadas). Nunca enviamos ficheiros, nomes de " +
        "documentos, endereços, capturas de ecrã ou conteúdo de pastas.\n\n" +
        "• Dados de conta: o seu nome e e-mail ficam guardados apenas neste computador, em " +
        "%AppData%\\HLOptimizerPro. A senha nunca é guardada em claro (PBKDF2-SHA256, 100 000 iterações com salt).\n\n" +
        "• Login com a Microsoft (opcional): usamos o MSAL com o navegador do sistema e não guardamos tokens em disco; " +
        "só o identificador da conta fica associado ao perfil local.\n\n" +
        "• Rede: o benchmark DNS envia consultas DNS normais aos 5 servidores listados para medir tempos de resposta. " +
        "A obtenção do IP público usa um serviço externo e é opcional (aparece como " +
        "«não disponível» se estiver offline).\n\n" +
        "• Logs: ficam no seu disco (pasta «logs») e são apagados automaticamente após 30 dias. " +
        "Pode exportá-los para pedir suporte ou apagá-los em «Configuração → Limpar histórico».";
}

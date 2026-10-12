using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>Pergunta frequente (FAQ) com resposta.</summary>
public sealed record FaqEntry(string Question, string Answer, string IconKind);

/// <summary>Passo do guia "Como Usar".</summary>
public sealed record HelpStep(int Number, string Title, string Description, string IconKind);

/// <summary>
/// ViewModel da Ajuda (3.9).
///
/// Conteúdo: perguntas frequentes (com respostas honestas sobre o que a aplicação faz e não faz),
/// guia "Como Usar" passo a passo, ligações de suporte (e-mail, site, vídeos), acesso direto à pasta
/// de logs e ao relatório de diagnóstico para anexar ao pedido de suporte.
/// </summary>
public sealed partial class HelpViewModel : ViewModelBase
{
    private readonly QuickActionsService _quickActions;
    private readonly IDialogService _dialogs;

    public HelpViewModel(
        NotificationService notifications,
        QuickActionsService quickActions,
        IDialogService dialogs)
        : base(notifications)
    {
        _quickActions = quickActions;
        _dialogs = dialogs;

        Title = "Ajuda";
        Subtitle = "Como usar, perguntas frequentes e apoio técnico";
        IconKind = "HelpCircle";

        BuildFaq();
        BuildSteps();
    }

    // ---- FAQ --------------------------------------------------------------------

    public ObservableCollection<FaqEntry> Faq { get; } = new();

    [ObservableProperty] private string _faqFilter = string.Empty;
    [ObservableProperty] private string _exportStatusText = string.Empty;

    public IEnumerable<FaqEntry> FilteredFaq => string.IsNullOrWhiteSpace(FaqFilter)
        ? Faq
        : Faq.Where(f => f.Question.Contains(FaqFilter, StringComparison.OrdinalIgnoreCase)
                         || f.Answer.Contains(FaqFilter, StringComparison.OrdinalIgnoreCase));

    partial void OnFaqFilterChanged(string value) => OnPropertyChanged(nameof(FilteredFaq));

    /// <summary>Passos do guia de utilização.</summary>
    public ObservableCollection<HelpStep> Steps { get; } = new();

    private void BuildFaq()
    {
        Faq.Add(new FaqEntry(
            "As otimizações são reversíveis?",
            "Sim. Antes de escrever qualquer valor, guardamos o valor original (e exportamos a chave de registo para .reg). " +
            "Pode reverter um ajuste, um lote ou tudo em «Otimizações» → «Reverter tudo». O diário mostra o que foi alterado e quando.",
            "History"));

        Faq.Add(new FaqEntry(
            "A aplicação precisa de ser administrador?",
            "Muitas otimizações escrevem em HKEY_LOCAL_MACHINE, alteram serviços ou criam pontos de restauro, o que exige elevação. " +
            "A aplicação pede-a no arranque (manifesto UAC); sem ela, as funcionalidades que precisam ficam desativadas e a interface avisa.",
            "ShieldAccountOutline"));

        Faq.Add(new FaqEntry(
            "Os números de CPU, RAM e GPU são reais?",
            "Sim — vêm de PerformanceCounter, WMI e (quando disponível) do LibreHardwareMonitor. " +
            "Se um sensor não existir na sua máquina, mostramos «N/D» em vez de inventar um valor.",
            "Speedometer"));

        Faq.Add(new FaqEntry(
            "O Modo Jogo pode fechar os meus programas?",
            "Não. O Modo Jogo apenas ajusta prioridades, afinidade de núcleos e o plano de energia, e limita a prioridade de processos " +
            "de fundo conhecidos. Nunca encerra processos e nunca toca na lista de processos críticos do Windows.",
            "ControllerClassic"));

        Faq.Add(new FaqEntry(
            "Para que serve o modo Simular?",
            "Mostra exatamente o que seria alterado — chave, valor atual e valor novo — sem escrever nada no sistema. " +
            "É a forma mais segura de ver o efeito antes de aplicar; ativa-se no interruptor «Simular» da barra de título.",
            "EyeOutline"));

        Faq.Add(new FaqEntry(
            "Enviam os meus dados para algum servidor?",
            "Não. Por predefinição não há telemetria e nada sai do computador. Só se ativar a telemetria na Configuração é que são enviados " +
            "dados anónimos de desempenho — e pode desligar a qualquer momento.",
            "ShieldLock"));

        Faq.Add(new FaqEntry(
            "Porque é que uma otimização aparece como «Indisponível»?",
            "Porque depende de algo que não existe nesta máquina — por exemplo, uma chave de registo que só existe no Windows 11 " +
            "ou um serviço que este Windows não tem. Nesse caso a aplicação explica o motivo em vez de falhar em silêncio.",
            "AlertCircleOutline"));

        Faq.Add(new FaqEntry(
            "Como restauro o computador ao estado anterior?",
            "Tem duas vias: o diário de alterações (reversão exata dos valores guardados) e o ponto de restauro do Windows " +
            "criado antes dos lotes de risco. Se algo correr mal, use primeiro o diário; o ponto de restauro é a rede de segurança final.",
            "History"));

        Faq.Add(new FaqEntry(
            "A aplicação funciona sem Internet?",
            "Sim. As medições, otimizações e reversões são locais. Só o benchmark DNS, o IP público e a validação online da licença " +
            "precisam de rede — e a licença continua válida 30 dias sem Internet (validação offline com cache).",
            "WifiOff"));

        Faq.Add(new FaqEntry(
            "Onde ficam os ficheiros da aplicação?",
            $"Em {AppPaths.Root}: preferências, histórico, diário de alterações, backups (.reg), relatórios e logs. " +
            "Nada é escrito fora desta pasta e de %TEMP%.",
            "Folder"));

        OnPropertyChanged(nameof(FilteredFaq));
    }

    private void BuildSteps()
    {
        Steps.Add(new HelpStep(1, "Avalie o sistema",
            "Comece em «Avaliação» e execute a análise. Vai receber uma pontuação 0–100 % e, em baixo, os problemas concretos encontrados.",
            "ChartBox"));

        Steps.Add(new HelpStep(2, "Veja as recomendações",
            "Em «Recomendações» tem a lista por prioridade. Aplique primeiro as de prioridade alta — são as que mais impacto têm.",
            "LightbulbOn"));

        Steps.Add(new HelpStep(3, "Use o PC Boost (opcional)",
            "Se prefere um plano pronto, o «PC Boost» pergunta para que usa o computador e monta o conjunto de otimizações adequado.",
            "RocketLaunch"));

        Steps.Add(new HelpStep(4, "Otimize quando quiser",
            "No Dashboard, o botão «Executar Otimização» faz a limpeza rápida e aplica as otimizações recomendadas de uma só vez.",
            "ViewDashboard"));

        Steps.Add(new HelpStep(5, "Jogue com o Modo Jogo",
            "Ative o Modo Jogo em «Ferramentas → Modo Jogo». A aplicação deteta o jogo sozinha e repõe tudo quando o fecha.",
            "ControllerClassic"));

        Steps.Add(new HelpStep(6, "Reveja e reverta",
            "Em «Otimizações» → «Exportar diário» vê tudo o que foi alterado. Se algo não correr bem, «Reverter tudo» repõe os valores originais.",
            "History"));
    }

    // ---- Suporte ------------------------------------------------------------------

    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _quickActions.ExportSystemLogsAsync();

            Report(result, "Suporte");

            if (result.Success)
            {
                ExportStatusText = $"Diagnóstico guardado em: {result.Value}";

                var open = await _dialogs.ConfirmAsync(
                    "Diagnóstico pronto",
                    $"{result.Value}\n\nAnexe este ficheiro ao seu pedido de suporte. Quer abrir a pasta?",
                    "Abrir pasta");

                if (open) _quickActions.OpenDataFolder();
            }
        }, "A recolher o diagnóstico…");
    }

    [RelayCommand]
    private void OpenSupport() => _dialogs.OpenInShell(AppConstants.SupportUrl);

    [RelayCommand]
    private void OpenDocs() => _dialogs.OpenInShell(AppConstants.DocsUrl);

    [RelayCommand]
    private void OpenVideos() => _dialogs.OpenInShell(AppConstants.VideosUrl);

    [RelayCommand]
    private void OpenLogsFolder() => _dialogs.OpenInShell(AppPaths.Logs);

    [RelayCommand]
    private async Task CopySupportEmailAsync()
    {
        try
        {
            System.Windows.Clipboard.SetText(AppConstants.SupportEmail);
            SetStatus($"Endereço de suporte copiado: {AppConstants.SupportEmail}");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SetStatus($"Não foi possível copiar o endereço: {ex.Message}", isError: true);
        }
    }

    public string SupportEmail => AppConstants.SupportEmail;
    public string SupportUrl => AppConstants.SupportUrl;
    public string DocsUrl => AppConstants.DocsUrl;
    public string VideosUrl => AppConstants.VideosUrl;
    public string VersionText => $"Versão {AppConstants.Version}";
}

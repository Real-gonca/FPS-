using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels.Tools;

/// <summary>
/// Tela "Ferramentas": hub de utilitários avançados (serviços, drivers, hosts, DNS,
/// planos de energia, reparo de rede, SFC/DISM e backup/restauração).
/// </summary>
/// <remarks>
/// <para>
/// <b>Classe parcial por seção.</b> A tela tem oito seções independentes; cada uma
/// vive em um arquivo próprio (<c>ToolsViewModel.Services.cs</c>,
/// <c>ToolsViewModel.Network.cs</c>, …). O estado compartilhado — log de saída,
/// privilégio, helper de execução — fica aqui, evitando um arquivo de 1.500 linhas
/// (SRP/KISS na organização do código).
/// </para>
/// <para>
/// <b>Ferramentas de sistema são destrutivas.</b> Tudo passa por
/// <see cref="ExecuteToolAsync"/>/>, que: exige confirmação quando configurado,
/// mostra o diálogo de progresso com log ao vivo, registra no histórico de ações e
/// nunca deixa uma exceção escapar para a UI.
/// </para>
/// </remarks>
public sealed partial class ToolsViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly IElevationService _elevation;
    private readonly ISettingsService _settings;
    private readonly IActionHistoryService _history;

    /// <summary>Cria o ViewModel de ferramentas.</summary>
    /// <param name="serviceManager">Serviços do Windows.</param>
    /// <param name="driverManager">Drivers.</param>
    /// <param name="hostsEditor">Editor do arquivo hosts.</param>
    /// <param name="dns">DNS.</param>
    /// <param name="networkTools">Reparo de rede.</param>
    /// <param name="powerPlans">Planos de energia.</param>
    /// <param name="repair">Reparo do sistema (SFC/DISM/chkdsk).</param>
    /// <param name="backup">Backup e restauração.</param>
    /// <param name="dialogs">Diálogos.</param>
    /// <param name="elevation">Elevação sob demanda.</param>
    /// <param name="settings">Configurações.</param>
    /// <param name="history">Histórico de ações.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public ToolsViewModel(
        IServiceManager serviceManager,
        IDriverManagerService driverManager,
        IHostsEditorService hostsEditor,
        IDnsService dns,
        INetworkToolsService networkTools,
        IPowerPlanService powerPlans,
        ISystemRepairService repair,
        IBackupService backup,
        IDialogService dialogs,
        IElevationService elevation,
        ISettingsService settings,
        IActionHistoryService history,
        ILocalizationService localization,
        ILogger<ToolsViewModel> logger)
        : base(localization, logger)
    {
        ServiceManager = serviceManager;
        DriverManager = driverManager;
        HostsEditor = hostsEditor;
        Dns = dns;
        NetworkTools = networkTools;
        PowerPlans = powerPlans;
        Repair = repair;
        Backup = backup;
        _dialogs = dialogs;
        _elevation = elevation;
        _settings = settings;
        _history = history;

        IsElevated = _elevation.IsElevated;

        DnsPresets = DnsConfiguration.Presets.All;
    }

    // ---------------------------------------------------------------------
    // Serviços injetados (usados pelas seções parciais)
    // ---------------------------------------------------------------------

    private IServiceManager ServiceManager { get; }

    private IDriverManagerService DriverManager { get; }

    private IHostsEditorService HostsEditor { get; }

    private IDnsService Dns { get; }

    private INetworkToolsService NetworkTools { get; }

    private IPowerPlanService PowerPlans { get; }

    private ISystemRepairService Repair { get; }

    private IBackupService Backup { get; }

    /// <summary>Log de saída das ferramentas (console interno da tela).</summary>
    public ObservableCollection<string> OutputLog { get; } = [];

    /// <summary>Indica se há saída no log.</summary>
    public bool HasOutput => OutputLog.Count > 0;

    /// <summary>Presets de DNS disponíveis.</summary>
    public IReadOnlyList<DnsPreset> DnsPresets { get; }

    /// <summary>Indica se o processo é administrador.</summary>
    [ObservableProperty]
    private bool _isElevated;

    /// <summary>Seção selecionada (persistida apenas para voltar ao mesmo ponto).</summary>
    [ObservableProperty]
    private string _selectedSection = "services";

    /// <summary>Carrega os dados iniciais de todas as seções leves.</summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsElevated = _elevation.IsElevated;

        await Task.WhenAll(
            LoadServicesAsync(),
            LoadDriversAsync(),
            LoadDnsAsync(),
            LoadHostsAsync(),
            LoadPowerPlansAsync(),
            LoadRestorePointsAsync()).ConfigureAwait(true);
    }

    /// <summary>Limpa o log de saída.</summary>
    [RelayCommand]
    private void ClearOutput()
    {
        OutputLog.Clear();
        OnPropertyChanged(nameof(HasOutput));
    }

    /// <summary>Reinicia o aplicativo como administrador.</summary>
    [RelayCommand]
    private async Task ElevateAsync()
    {
        if (IsElevated)
        {
            return;
        }

        var restarted = await _elevation.RestartElevatedAsync().ConfigureAwait(true);

        if (restarted)
        {
            System.Windows.Application.Current?.Shutdown();
        }
        else
        {
            await _dialogs.ShowWarningAsync(L("Dlg_ElevationTitle"), L("Msg_NeedAdmin")).ConfigureAwait(true);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers compartilhados pelas seções
    // ---------------------------------------------------------------------

    /// <summary>
    /// Executa uma ferramenta que produz <see cref="CommandResult"/>, com diálogo de
    /// progresso, log ao vivo, registro no histórico e tratamento de erro.
    /// </summary>
    /// <param name="tool">Função que executa a ferramenta.</param>
    /// <param name="operationName">Nome exibido no diálogo e no histórico.</param>
    /// <param name="kind">Tipo da ação para o histórico.</param>
    /// <param name="requiresAdmin">Se exige administrador (bloqueia com aviso).</param>
    /// <returns>True quando a ferramenta foi executada com sucesso.</returns>
    private async Task<bool> ExecuteToolAsync(
        Func<IProgress<string>?, CancellationToken, Task<CommandResult>> tool,
        string operationName,
        ActionKind kind,
        bool requiresAdmin = false)
    {
        if (IsBusy)
        {
            return false;
        }

        if (requiresAdmin && !IsElevated)
        {
            await _dialogs.ShowWarningAsync(L("Dlg_ElevationTitle"), L("Tools_RequiresAdmin")).ConfigureAwait(true);
            return false;
        }

        CommandResult? result = null;

        var completed = await _dialogs.ShowProgressAsync(
            operationName,
            async (progress, cancellationToken) =>
            {
                var textProgress = new Progress<string>(line =>
                {
                    AppendOutput(line);
                    progress.Report(new ScanProgress(IssueCategory.Performance, operationName, 0, 0, line));
                });

                result = await tool(textProgress, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!completed || result is null)
        {
            StatusMessage = L("Dlg_CancelledTitle");
            return false;
        }

        AppendOutput($"[{operationName}] exit={result.ExitCode} · {result.Duration.TotalSeconds:F1}s · elevated={result.WasElevated}");

        if (!string.IsNullOrWhiteSpace(result.CombinedOutput))
        {
            AppendOutput(TrimOutput(result.CombinedOutput));
        }

        await _history.RecordAsync(
            kind,
            operationName,
            details: result.IsSuccess ? result.CombinedOutput : result.StandardError,
            durationMs: (long)result.Duration.TotalMilliseconds,
            success: result.IsSuccess).ConfigureAwait(true);

        StatusMessage = result.IsSuccess
            ? LF("Tools_CompletedSuccess", operationName)
            : LF("Tools_CompletedWithErrors", operationName, result.ExitCode);

        return result.IsSuccess;
    }

    /// <summary>
    /// Executa uma operação simples (true/false) com diálogo de progresso curto.
    /// </summary>
    /// <param name="operation">Operação.</param>
    /// <param name="operationName">Nome exibido.</param>
    /// <param name="kind">Tipo da ação.</param>
    /// <param name="requiresAdmin">Se exige administrador.</param>
    /// <param name="successMessage">Mensagem de sucesso (chave de recurso).</param>
    /// <returns>Resultado da operação.</returns>
    private async Task<bool> ExecuteSimpleAsync(
        Func<CancellationToken, Task<bool>> operation,
        string operationName,
        ActionKind kind,
        bool requiresAdmin = false,
        string? successMessage = null)
    {
        if (IsBusy)
        {
            return false;
        }

        if (requiresAdmin && !IsElevated)
        {
            await _dialogs.ShowWarningAsync(L("Dlg_ElevationTitle"), L("Tools_RequiresAdmin")).ConfigureAwait(true);
            return false;
        }

        var outcome = false;

        var completed = await _dialogs.ShowProgressAsync(
            operationName,
            async (_, cancellationToken) =>
            {
                outcome = await operation(cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!completed)
        {
            StatusMessage = L("Dlg_CancelledTitle");
            return false;
        }

        await _history.RecordAsync(kind, operationName, success: outcome).ConfigureAwait(true);

        AppendOutput($"[{operationName}] {(outcome ? "OK" : "FALHOU")}");

        StatusMessage = outcome
            ? (successMessage is null ? LF("Tools_CompletedSuccess", operationName) : L(successMessage))
            : L("Msg_GenericError");

        return outcome;
    }

    /// <summary>Confirma uma ação destrutiva antes de executá-la.</summary>
    /// <param name="title">Título.</param>
    /// <param name="message">Mensagem.</param>
    /// <param name="confirmText">Rótulo do botão de confirmação.</param>
    /// <returns>True quando o usuário confirmou.</returns>
    private Task<bool> ConfirmAsync(string title, string message, string? confirmText = null) =>
        _dialogs.ConfirmAsync(title, message, confirmText ?? L("Common_Apply"), isDestructive: true);

    /// <summary>Adiciona uma linha ao log de saída (limite de 500 linhas).</summary>
    private void AppendOutput(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        OnUiThread(() =>
        {
            foreach (var part in line.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                OutputLog.Add($"{DateTime.Now:HH:mm:ss}  {part.TrimEnd('\r')}");
            }

            while (OutputLog.Count > 500)
            {
                OutputLog.RemoveAt(0);
            }

            OnPropertyChanged(nameof(HasOutput));
        });
    }

    /// <summary>Limita o tamanho da saída exibida (ferramentas podem gerar MBs de texto).</summary>
    private static string TrimOutput(string output, int maxLines = 60)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return lines.Length <= maxLines
            ? output.Trim()
            : string.Join(Environment.NewLine, lines.Take(maxLines)) + Environment.NewLine + $"… (+{lines.Length - maxLines} linhas)";
    }

    /// <summary>Indica se a configuração exige confirmação antes de alterações destrutivas.</summary>
    private bool RequiresConfirmation => _settings.Current.ConfirmBeforeDelete;

    /// <summary>
    /// Abre um arquivo, snap-in (.msc) ou URL com o handler padrão do Windows.
    /// </summary>
    /// <param name="target">Caminho, documento ou URL.</param>
    /// <param name="arguments">Argumentos opcionais.</param>
    private async Task OpenExternalAsync(string target, string? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            await Task.Run(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true
            })).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao abrir {Target}.", target);

            await _dialogs.ShowErrorAsync(L("Dlg_ErrorTitle"), L("Tools_CannotOpenTarget")).ConfigureAwait(true);
        }
    }
}

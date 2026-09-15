using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// Tela "Otimização": escolha do modo (Rápido/Completo/Gamer/Privacidade),
/// ajustes finos, pré-visualização do plano de execução e resultado consolidado.
/// </summary>
/// <remarks>
/// <para>
/// <b>Plano antes de executar.</b> <see cref="IOptimizationService.GetPlan"/> devolve
/// os passos que <i>seriam</i> executados para as opções atuais — exibimos essa lista
/// antes do clique final (transparência é parte do produto).
/// </para>
/// <para>
/// <b>Toggles → modelo.</b> As opções ficam expostas como propriedades observáveis
/// individuais (para binding simples no XAML) e são sincronizadas num
/// <see cref="OptimizationOptions"/> apenas no momento da execução
/// (<see cref="BuildOptions"/>), evitando estado duplicado divergente.
/// </para>
/// </remarks>
public sealed partial class OptimizationViewModel : ViewModelBase
{
    private readonly IOptimizationService _optimization;
    private readonly IDialogService _dialogs;
    private readonly IElevationService _elevation;
    private readonly ISettingsService _settings;
    private readonly IActionHistoryService _history;

    /// <summary>Cria o ViewModel da otimização.</summary>
    /// <param name="optimization">Orquestrador dos passos.</param>
    /// <param name="dialogs">Diálogos.</param>
    /// <param name="elevation">Elevação sob demanda.</param>
    /// <param name="settings">Configurações (nível/modo padrão).</param>
    /// <param name="history">Histórico de ações.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public OptimizationViewModel(
        IOptimizationService optimization,
        IDialogService dialogs,
        IElevationService elevation,
        ISettingsService settings,
        IActionHistoryService history,
        ILocalizationService localization,
        ILogger<OptimizationViewModel> logger)
        : base(localization, logger)
    {
        _optimization = optimization;
        _dialogs = dialogs;
        _elevation = elevation;
        _settings = settings;
        _history = history;

        Modes.Add(new ModeCard(OptimizationMode.Quick, L("Opt_Mode_Quick"), L("Opt_Mode_Quick_Desc"), "\u21AF"));
        Modes.Add(new ModeCard(OptimizationMode.Full, L("Opt_Mode_Full"), L("Opt_Mode_Full_Desc"), "\u2697"));
        Modes.Add(new ModeCard(OptimizationMode.Gamer, L("Opt_Mode_Gamer"), L("Opt_Mode_Gamer_Desc"), "\u2694"));
        Modes.Add(new ModeCard(OptimizationMode.Privacy, L("Opt_Mode_Privacy"), L("Opt_Mode_Privacy_Desc"), "\u26E8"));

        IsElevated = _elevation.IsElevated;

        var defaultMode = _settings.Current.DefaultOptimizationMode;

        SelectedMode = Modes.FirstOrDefault(m => m.Mode == defaultMode) ?? Modes[1];
        Level = _settings.Current.OptimizationLevel;

        ApplyModeDefaults(SelectedMode.Mode);
        RefreshPlan();
    }

    /// <summary>Cartões de modo disponíveis.</summary>
    public ObservableCollection<ModeCard> Modes { get; } = [];

    /// <summary>Níveis de agressividade (combo de opções).</summary>
    public IReadOnlyList<OptimizationLevel> Levels { get; } = Enum.GetValues<OptimizationLevel>();

    /// <summary>Passos do plano atual (pré-visualização).</summary>
    public ObservableCollection<PlanStep> PlanSteps { get; } = [];

    /// <summary>Log em tempo real da última execução.</summary>
    public ObservableCollection<string> LiveLog { get; } = [];

    /// <summary>Modo selecionado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedModeTitle))]
    private ModeCard? _selectedMode;

    /// <summary>Título do modo selecionado.</summary>
    public string SelectedModeTitle => SelectedMode?.Title ?? string.Empty;

    /// <summary>Nível de agressividade.</summary>
    [ObservableProperty]
    private OptimizationLevel _level;

    /// <summary>Limpar arquivos temporários.</summary>
    [ObservableProperty]
    private bool _cleanTemporaryFiles = true;

    /// <summary>Limpar cache do sistema.</summary>
    [ObservableProperty]
    private bool _cleanSystemCache = true;

    /// <summary>Otimizar registro.</summary>
    [ObservableProperty]
    private bool _optimizeRegistry = true;

    /// <summary>Flush de DNS.</summary>
    [ObservableProperty]
    private bool _flushDns = true;

    /// <summary>Otimizar serviços.</summary>
    [ObservableProperty]
    private bool _optimizeServices;

    /// <summary>Desativar telemetria.</summary>
    [ObservableProperty]
    private bool _disableTelemetry = true;

    /// <summary>Criar ponto de restauração.</summary>
    [ObservableProperty]
    private bool _createRestorePoint = true;

    /// <summary>Ativar desempenho máximo.</summary>
    [ObservableProperty]
    private bool _enableMaximumPerformance = true;

    /// <summary>Esvaziar lixeira.</summary>
    [ObservableProperty]
    private bool _emptyRecycleBin;

    /// <summary>Limpar cache de navegadores.</summary>
    [ObservableProperty]
    private bool _cleanBrowserCache;

    /// <summary>Otimizar inicialização.</summary>
    [ObservableProperty]
    private bool _optimizeStartup;

    /// <summary>Ajustes de rede para baixa latência.</summary>
    [ObservableProperty]
    private bool _optimizeNetworkLatency;

    /// <summary>Reiniciar o Explorer ao final.</summary>
    [ObservableProperty]
    private bool _restartExplorer;

    /// <summary>Indica se o processo é administrador.</summary>
    [ObservableProperty]
    private bool _isElevated;

    /// <summary>Quantidade de passos do plano.</summary>
    [ObservableProperty]
    private int _planCount;

    /// <summary>Indica se há um resultado para exibir.</summary>
    [ObservableProperty]
    private bool _hasResult;

    /// <summary>Passos bem-sucedidos na última execução.</summary>
    [ObservableProperty]
    private int _successCount;

    /// <summary>Passos ignorados na última execução.</summary>
    [ObservableProperty]
    private int _skippedCount;

    /// <summary>Passos com falha na última execução.</summary>
    [ObservableProperty]
    private int _failedCount;

    /// <summary>Espaço liberado na última execução.</summary>
    [ObservableProperty]
    private string _freedText = "0 MB";

    /// <summary>Duração da última execução.</summary>
    [ObservableProperty]
    private string _durationText = "—";

    /// <summary>Indica se a última execução pediu elevação.</summary>
    [ObservableProperty]
    private bool _resultRequiresElevation;

    /// <summary>Executa a otimização com as opções atuais.</summary>
    [RelayCommand]
    private async Task OptimizeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var options = BuildOptions();

        if (_settings.Current.ConfirmBeforeDelete && options.CleanTemporaryFiles)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                L("Opt_OptimizeNow"),
                LF("Opt_ConfirmMessage", Localization.GetEnumText(options.Mode), PlanCount)).ConfigureAwait(true);

            if (!confirmed)
            {
                StatusMessage = L("Dlg_CancelledTitle");
                return;
            }
        }

        OptimizationResult? result = null;

        LiveLog.Clear();

        var completed = await _dialogs.ShowProgressAsync(
            L("Opt_Running"),
            async (progress, cancellationToken) =>
            {
                var forwarding = new Progress<ScanProgress>(p =>
                {
                    progress.Report(p);

                    AppendLog(p.Message);
                });

                result = await _optimization.OptimizeAsync(options, forwarding, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!completed || result is null)
        {
            StatusMessage = L("Dlg_CancelledTitle");
            return;
        }

        ApplyResult(result);

        await _dialogs.ShowOptimizationResultAsync(result).ConfigureAwait(true);

        await _history.RecordOptimizationAsync(result).ConfigureAwait(true);
    }

    /// <summary>Reverte os passos reversíveis do modo selecionado.</summary>
    [RelayCommand]
    private async Task RevertAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var mode = SelectedMode?.Mode ?? OptimizationMode.Full;

        var confirmed = await _dialogs.ConfirmAsync(
            L("Opt_RestoreDefaults"),
            LF("Opt_RestoreConfirm", Localization.GetEnumText(mode)),
            isDestructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        OptimizationResult? result = null;

        var completed = await _dialogs.ShowProgressAsync(
            L("Opt_RestoreDefaults"),
            async (progress, cancellationToken) =>
            {
                result = await _optimization.RevertAsync(mode, progress, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (completed && result is not null)
        {
            ApplyResult(result);

            await _dialogs.ShowOptimizationResultAsync(result).ConfigureAwait(true);
        }
    }

    /// <summary>Recalcula a pré-visualização do plano.</summary>
    [RelayCommand]
    private void RefreshPlan()
    {
        try
        {
            var plan = _optimization.GetPlan(BuildOptions());

            PlanSteps.Clear();

            foreach (var step in plan.OrderBy(s => s.Order))
            {
                PlanSteps.Add(new PlanStep(step.Id, step.Name, step.Description, step.RequiresAdmin, step.IsReversible));
            }

            PlanCount = PlanSteps.Count;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao montar a pré-visualização do plano de otimização.");

            PlanSteps.Clear();
            PlanCount = 0;
        }
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

    /// <summary>Limpa o log em tempo real.</summary>
    [RelayCommand]
    private void ClearLog() => LiveLog.Clear();

    partial void OnSelectedModeChanged(ModeCard? value)
    {
        if (value is null)
        {
            return;
        }

        ApplyModeDefaults(value.Mode);
        RefreshPlan();
    }

    partial void OnLevelChanged(OptimizationLevel value) => RefreshPlan();

    // Os toggles só afetam o plano; recalculamos em bloco para evitar ruído.
    partial void OnCleanTemporaryFilesChanged(bool value) => RefreshPlan();
    partial void OnCleanSystemCacheChanged(bool value) => RefreshPlan();
    partial void OnOptimizeRegistryChanged(bool value) => RefreshPlan();
    partial void OnFlushDnsChanged(bool value) => RefreshPlan();
    partial void OnOptimizeServicesChanged(bool value) => RefreshPlan();
    partial void OnDisableTelemetryChanged(bool value) => RefreshPlan();
    partial void OnCreateRestorePointChanged(bool value) => RefreshPlan();
    partial void OnEnableMaximumPerformanceChanged(bool value) => RefreshPlan();
    partial void OnEmptyRecycleBinChanged(bool value) => RefreshPlan();
    partial void OnCleanBrowserCacheChanged(bool value) => RefreshPlan();
    partial void OnOptimizeStartupChanged(bool value) => RefreshPlan();
    partial void OnOptimizeNetworkLatencyChanged(bool value) => RefreshPlan();
    partial void OnRestartExplorerChanged(bool value) => RefreshPlan();

    /// <summary>Aplica os toggles padrão de um modo.</summary>
    private void ApplyModeDefaults(OptimizationMode mode)
    {
        var defaults = mode switch
        {
            OptimizationMode.Quick => OptimizationOptions.ForQuick(),
            OptimizationMode.Gamer => OptimizationOptions.ForGamer(),
            OptimizationMode.Privacy => OptimizationOptions.ForPrivacy(),
            _ => OptimizationOptions.ForFull()
        };

        CleanTemporaryFiles = defaults.CleanTemporaryFiles;
        CleanSystemCache = defaults.CleanSystemCache;
        OptimizeRegistry = defaults.OptimizeRegistry;
        FlushDns = defaults.FlushDns;
        OptimizeServices = defaults.OptimizeServices;
        DisableTelemetry = defaults.DisableTelemetry;
        CreateRestorePoint = defaults.CreateRestorePoint && _settings.Current.CreateRestorePointBeforeChanges;
        EnableMaximumPerformance = defaults.EnableMaximumPerformance;
        EmptyRecycleBin = defaults.EmptyRecycleBin;
        CleanBrowserCache = defaults.CleanBrowserCache;
        OptimizeStartup = defaults.OptimizeStartup;
        OptimizeNetworkLatency = defaults.OptimizeNetworkLatency;
        RestartExplorer = defaults.RestartExplorer;
    }

    /// <summary>Monta as opções a partir do estado atual da tela.</summary>
    private OptimizationOptions BuildOptions() => new()
    {
        Mode = SelectedMode?.Mode ?? OptimizationMode.Full,
        Level = Level,
        CleanTemporaryFiles = CleanTemporaryFiles,
        CleanSystemCache = CleanSystemCache,
        OptimizeRegistry = OptimizeRegistry,
        FlushDns = FlushDns,
        OptimizeServices = OptimizeServices,
        DisableTelemetry = DisableTelemetry,
        CreateRestorePoint = CreateRestorePoint,
        EnableMaximumPerformance = EnableMaximumPerformance,
        EmptyRecycleBin = EmptyRecycleBin,
        CleanBrowserCache = CleanBrowserCache,
        OptimizeStartup = OptimizeStartup,
        OptimizeNetworkLatency = OptimizeNetworkLatency,
        RestartExplorer = RestartExplorer
    };

    private void ApplyResult(OptimizationResult result)
    {
        HasResult = true;
        SuccessCount = result.SuccessCount;
        SkippedCount = result.SkippedCount;
        FailedCount = result.FailedCount;
        FreedText = result.FreedFormatted;
        DurationText = result.Duration.ToString(result.Duration.TotalMinutes >= 1 ? @"m\m\ s\s" : @"s\.fff\s");
        ResultRequiresElevation = result.RequiresElevation;

        StatusMessage = result.Summary;

        foreach (var step in result.Steps)
        {
            AppendLog($"{(step.Success ? "✓" : step.Skipped ? "→" : "✗")} {step.StepName}: {step.Message}");
        }
    }

    /// <summary>Adiciona uma linha ao log mantendo o limite de 400 entradas.</summary>
    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        OnUiThread(() =>
        {
            LiveLog.Add($"{DateTime.Now:HH:mm:ss}  {message}");

            while (LiveLog.Count > 400)
            {
                LiveLog.RemoveAt(0);
            }
        });
    }
}

/// <summary>Cartão de modo de otimização.</summary>
/// <param name="Mode">Modo.</param>
/// <param name="Title">Título localizado.</param>
/// <param name="Description">Descrição localizada.</param>
/// <param name="Glyph">Ícone (Segoe UI Symbol).</param>
public sealed record ModeCard(OptimizationMode Mode, string Title, string Description, string Glyph);

/// <summary>Passo do plano de otimização (pré-visualização).</summary>
/// <param name="Id">Identificador do passo.</param>
/// <param name="Name">Nome legível.</param>
/// <param name="Description">Efeito produzido.</param>
/// <param name="RequiresAdmin">Se exige administrador.</param>
/// <param name="IsReversible">Se pode ser revertido.</param>
public sealed record PlanStep(string Id, string Name, string Description, bool RequiresAdmin, bool IsReversible);

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>
/// Linha ajustável do medidor de latência. Envolve um <see cref="LatencyComponent"/> do serviço
/// para que o slider atualize o texto de estado em tempo real (o modelo do Core não é observável).
/// </summary>
public sealed partial class LatencySliderRow : ObservableObject
{
    public LatencySliderRow(LatencyComponent component)
    {
        Component = component;
        _value = component.Value;
    }

    public LatencyComponent Component { get; }

    public string Key => Component.Key;
    public string Name => Component.Name;
    public string Description => Component.Description;
    public string IconKind => Component.IconKind;
    public string LowLabel => Component.LowLabel;
    public string HighLabel => Component.HighLabel;

    [ObservableProperty] private double _value;

    /// <summary>Texto que descreve o nível escolhido (Desativado → Máximo).</summary>
    public string ValueText => Component.ValueText;

    /// <summary>True quando o ajuste vai ter efeito (slider à direita do ponto neutro).</summary>
    public bool WillApply => Component.Value >= 45;

    public string ApplyText => WillApply ? "Será ajustado" : "Sem alterações";

    public HealthLevel ApplyHealth => Component.Value switch
    {
        >= 70 => HealthLevel.Otimo,
        >= 45 => HealthLevel.Atencao,
        _ => HealthLevel.Desconhecido,
    };

    partial void OnValueChanged(double value)
    {
        Component.Value = value;

        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(WillApply));
        OnPropertyChanged(nameof(ApplyText));
        OnPropertyChanged(nameof(ApplyHealth));
    }
}

/// <summary>
/// ViewModel do medidor de Latência (3.4-f).
///
/// Medição: latência real ida-e-volta (ICMP a 1.1.1.1 e ao gateway), jitter, DPC/ISR do kernel e
/// resolução do temporizador do sistema. Nada disto é estimado — vem de medições no computador.
///
/// Ajustes: seis sliders (Rato/Entrada, CPU, Kernel, GPU, Rede, Tela). Cada slider à direita do
/// ponto neutro (≥ 45) gera tweaks concretos: resolução do temporizador, MMCSS/scheduling da CPU,
/// Nagle/TCPNoDelay na interface ativa, prioridade da GPU e timeout do monitor. Tudo passa pelo motor
/// de otimizações com backup e reversão.
/// </summary>
public sealed partial class LatencyViewModel : ViewModelBase
{
    private readonly LatencyService _latency;
    private readonly OptimizationCatalog _catalog;
    private readonly OptimizationEngine _engine;
    private readonly OptimizationJournal _journal;
    private readonly IDialogService _dialogs;

    public LatencyViewModel(
        NotificationService notifications,
        LatencyService latency,
        OptimizationCatalog catalog,
        OptimizationEngine engine,
        OptimizationJournal journal,
        IDialogService dialogs)
        : base(notifications)
    {
        _latency = latency;
        _catalog = catalog;
        _engine = engine;
        _journal = journal;
        _dialogs = dialogs;

        Title = "Latência";
        Subtitle = "Meça e reduza o tempo de resposta do sistema, do rato e da rede";
        IconKind = "TimerOutline";

        foreach (var component in _latency.Components)
            Sliders.Add(new LatencySliderRow(component));
    }

    // ---- Medições -------------------------------------------------------------

    [ObservableProperty] private double _currentLatencyMs;
    [ObservableProperty] private double _jitterMs;
    [ObservableProperty] private double _dpcLatencyUs;
    [ObservableProperty] private double _timerResolutionMs;
    [ObservableProperty] private double _networkLatencyMs;
    [ObservableProperty] private string _currentLatencyText = "—";
    [ObservableProperty] private string _jitterText = "—";
    [ObservableProperty] private string _dpcText = "—";
    [ObservableProperty] private string _timerText = "—";
    [ObservableProperty] private string _networkText = "—";
    [ObservableProperty] private string _qualityText = "A medir…";
    [ObservableProperty] private HealthLevel _qualityHealth = HealthLevel.Desconhecido;
    [ObservableProperty] private string _lastMeasureText = "Ainda não mediu nesta sessão.";
    [ObservableProperty] private string _previewText = string.Empty;
    [ObservableProperty] private string _lastApplyText = "Ainda não aplicou ajustes nesta sessão.";
    [ObservableProperty] private string _highResolutionTimerText = "Temporizador do sistema: —";

    /// <summary>Os seis componentes ajustáveis.</summary>
    public ObservableCollection<LatencySliderRow> Sliders { get; } = new();

    /// <summary>Número de sliders que vão produzir alterações.</summary>
    public int PendingAdjustments => Sliders.Count(s => s.WillApply);

    // ---- Medição --------------------------------------------------------------

    [RelayCommand]
    private async Task MeasureAsync()
    {
        await RunBusyAsync(async () =>
        {
            BusyMessage = "A medir latência à Internet…";

            var status = await _latency.MeasureAsync();

            CurrentLatencyMs = status.CurrentLatencyMs;
            JitterMs = status.JitterMs;
            DpcLatencyUs = status.DpcLatencyUs;
            TimerResolutionMs = status.TimerResolutionMs;
            NetworkLatencyMs = status.NetworkLatencyMs;

            CurrentLatencyText = $"{status.CurrentLatencyMs:0.0} ms";
            JitterText = $"{status.JitterMs:0.0} ms";
            DpcText = status.DpcLatencyUs <= 0
                ? "não disponível"
                : $"{status.DpcLatencyUs:0.0} µs por interrupção";
            TimerText = $"{status.TimerResolutionMs:0.000} ms";
            NetworkText = status.NetworkLatencyMs <= 0 ? "não medido" : $"{status.NetworkLatencyMs:0.0} ms";
            QualityText = status.QualityText;
            QualityHealth = status.Health;

            LastMeasureText = $"Medição de {DateTime.Now:HH:mm:ss} — " +
                              $"ping {status.CurrentLatencyMs:0.0} ms, jitter {status.JitterMs:0.0} ms, " +
                              $"temporizador {status.TimerResolutionMs:0.000} ms.";

            RefreshPreview();
            SetStatus(LastMeasureText);
        }, "A medir a latência…");
    }

    /// <summary>Atualiza a descrição do que será aplicado com os sliders atuais.</summary>
    private void RefreshPreview()
    {
        OnPropertyChanged(nameof(PendingAdjustments));

        var preview = _latency.Preview();
        PreviewText = string.Join("\n", preview);
    }

    partial void OnPendingAdjustmentsChanged(int value) => RefreshPreview();

    /// <summary>Chamado pelos sliders (via comando) para atualizar a pré-visualização.</summary>
    [RelayCommand]
    private void RefreshAdjustments() => RefreshPreview();

    // ---- Aplicar ---------------------------------------------------------------

    /// <summary>Executa os ajustes do motor de latência (resolução do temporizador, MMCSS, Nagle, GPU...).</summary>
    [RelayCommand]
    private async Task ApplyAdjustmentsAsync()
    {
        var simulation = _catalog.SimulationMode;
        var actions = _latency.BuildActions(simulation);

        if (actions.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Sem alterações",
                "Os sliders estão na posição padrão, por isso não há nada a aplicar. " +
                "Mova um slider para a direita (a partir de 45 %) para ativar o ajuste correspondente.");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Aplicar ajustes de latência",
            $"{actions.Count} ajustes vão ser aplicados:\n\n" +
            string.Join("\n", actions.Select(a => $"• {a.Title} — {a.Risk}")) +
            "\n\nEstes ajustes alteram a resolução do temporizador do sistema e parâmetros de rede/CPU. " +
            "Todos guardam o valor original e podem ser revertidos com «Reverter ajustes».",
            "Aplicar ajustes");

        if (!confirmed) return;

        await RunBusyAsync(async () =>
        {
            _engine.ProgressChanged += OnEngineProgress;

            try
            {
                var result = await _engine.ApplyAsync(actions, new OptimizationRunOptions
                {
                    BatchName = "Latência",
                    Simulation = simulation,
                    CreateRestorePoint = false,   // ajustes imediatamente reversíveis
                    ActivityCategory = "Latência",
                });

                if (simulation)
                    await _dialogs.ShowSimulationAsync("Modo Simular — latência", _engine.Preview(actions));

                Report(result, "Latência");

                LastApplyText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
                _catalog.Refresh();
            }
            finally
            {
                _engine.ProgressChanged -= OnEngineProgress;
            }
        }, "A aplicar os ajustes de latência…");
    }

    private void OnEngineProgress(object? sender, OptimizationProgress progress)
        => BusyMessage = $"[{progress.Index}/{progress.Total}] {progress.ActionTitle}";

    /// <summary>Flag de estado do pedido de resolução máxima do temporizador.</summary>
    private bool _highResolutionRequested;

    /// <summary>Pede ao Windows a resolução máxima do temporizador enquanto a aplicação está aberta.</summary>
    [RelayCommand]
    private void ToggleHighResolutionTimer()
    {
        _highResolutionRequested = !_highResolutionRequested;
        var enable = _highResolutionRequested;

        var result = _latency.RequestHighResolutionTimer(enable);
        Report(result, "Latência");

        HighResolutionTimerText = enable
            ? "Temporizador do sistema: resolução máxima (0,5 ms) enquanto a aplicação estiver aberta."
            : "Temporizador do sistema: valor padrão do Windows.";
    }

    /// <summary>Reverte os ajustes de latência aplicados.</summary>
    [RelayCommand]
    private async Task RevertAsync()
    {
        var active = _journal.ActiveEntries.Where(e => e.ActionKey.StartsWith("latency.", StringComparison.OrdinalIgnoreCase)
                                                       || e.ActionKey.StartsWith("network.", StringComparison.OrdinalIgnoreCase))
                                           .ToList();

        if (active.Count == 0)
        {
            await _dialogs.ShowInfoAsync("Nada para reverter",
                "Não existem ajustes de latência aplicados no diário de alterações.");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Reverter ajustes de latência",
            $"Repor os valores originais de {active.Count} ajuste(s)?",
            "Reverter");

        if (!confirmed) return;

        await RunBusyAsync(async () =>
        {
            var result = await _engine.RevertAllAsync();
            Report(result, "Latência");

            LastApplyText = $"{DateTime.Now:HH:mm:ss} — {result.Message}";
            _catalog.Refresh();
        }, "A reverter os ajustes…");
    }

    public override async Task OnNavigatedToAsync(object? parameter = null)
    {
        await base.OnNavigatedToAsync(parameter);

        RefreshPreview();
        await MeasureAsync();
    }
}

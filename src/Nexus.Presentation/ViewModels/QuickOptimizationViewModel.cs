using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexus.Application.Optimization;
using Nexus.Application.UseCases;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Presentation.ViewModels;

/// <summary>
/// Otimização Rápida (spec §4.2): tweaks seguros num clique, com
/// micro-benchmark REAL medido antes/depois e rollback individual.
/// Sem promessas de "performance do sistema" — os números apresentados são
/// as medições do micro-benchmark, etiquetadas como tal.
/// </summary>
public sealed class QuickOptimizationViewModel : ObservableObject
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    public enum ApplyState { NotApplied, InProgress, Applied, Failed, Blocked }

    private readonly RunOptimizationUseCase _runOptimization;
    private readonly RollbackUseCase _rollback;
    private readonly BenchmarkUseCase _benchmark;
    private readonly IOptimizationTaskCatalog _catalog;
    private readonly INotificationHub _notifications;
    private readonly ILogger<QuickOptimizationViewModel> _log;

    private bool _isRunning;
    private string _statusText = string.Empty;
    private BenchmarkResult? _before;
    private BenchmarkResult? _after;

    public QuickOptimizationViewModel(
        RunOptimizationUseCase runOptimization,
        RollbackUseCase rollback,
        BenchmarkUseCase benchmark,
        IOptimizationTaskCatalog catalog,
        INotificationHub notifications,
        ILogger<QuickOptimizationViewModel> log)
    {
        _runOptimization = runOptimization;
        _rollback = rollback;
        _benchmark = benchmark;
        _catalog = catalog;
        _notifications = notifications;
        _log = log;

        Items = new ObservableCollection<QuickItemRow>(
            QuickOptimizationSet.Items.Select(i => new QuickItemRow(i, _catalog.Has(i.TaskKey))));

        RunCommand = new AsyncRelayCommand(RunAsync);
        RevertCommand = new AsyncRelayCommand<QuickItemRow>(RevertAsync);
    }

    public ObservableCollection<QuickItemRow> Items { get; }

    public IAsyncRelayCommand RunCommand { get; }

    public IAsyncRelayCommand<QuickItemRow> RevertCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
                OnPropertyChanged(nameof(RunButtonText));
        }
    }

    public string RunButtonText => IsRunning ? "A executar…" : "Executar Otimização Rápida";

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public BenchmarkResult? Before
    {
        get => _before;
        private set
        {
            if (Set(ref _before, value))
                RaiseBenchmarkChanged();
        }
    }

    public BenchmarkResult? After
    {
        get => _after;
        private set
        {
            if (Set(ref _after, value))
                RaiseBenchmarkChanged();
        }
    }

    public bool BenchmarkVisible => Before is not null || After is not null;

    public string CpuBefore => FormatCpu(Before?.CpuIndex);
    public string CpuAfter => FormatCpu(After?.CpuIndex);
    public string CpuDelta => DeltaPercent(Before?.CpuIndex, After?.CpuIndex);
    public string MemBefore => FormatMb(Before?.MemAllocMbPerSec);
    public string MemAfter => FormatMb(After?.MemAllocMbPerSec);
    public string MemDelta => DeltaPercent(Before?.MemAllocMbPerSec, After?.MemAllocMbPerSec);
    public string ReadBefore => FormatMb(Before?.DiskReadMbPerSec);
    public string ReadAfter => FormatMb(After?.DiskReadMbPerSec);
    public string ReadDelta => DeltaPercent(Before?.DiskReadMbPerSec, After?.DiskReadMbPerSec);
    public string WriteBefore => FormatMb(Before?.DiskWriteMbPerSec);
    public string WriteAfter => FormatMb(After?.DiskWriteMbPerSec);
    public string WriteDelta => DeltaPercent(Before?.DiskWriteMbPerSec, After?.DiskWriteMbPerSec);

    private void RaiseBenchmarkChanged()
    {
        OnPropertyChanged(nameof(BenchmarkVisible));
        OnPropertyChanged(nameof(CpuBefore));
        OnPropertyChanged(nameof(CpuAfter));
        OnPropertyChanged(nameof(CpuDelta));
        OnPropertyChanged(nameof(MemBefore));
        OnPropertyChanged(nameof(MemAfter));
        OnPropertyChanged(nameof(MemDelta));
        OnPropertyChanged(nameof(ReadBefore));
        OnPropertyChanged(nameof(ReadAfter));
        OnPropertyChanged(nameof(ReadDelta));
        OnPropertyChanged(nameof(WriteBefore));
        OnPropertyChanged(nameof(WriteAfter));
        OnPropertyChanged(nameof(WriteDelta));
    }

    private static string FormatCpu(double? v) =>
        v is null ? "N/D" : v.Value.ToString("0.0", Pt);

    private static string FormatMb(double? v) =>
        v is null ? "N/D" : v.Value.ToString("0", Pt) + " MB/s";

    private static string DeltaPercent(double? a, double? b)
    {
        if (a is null || b is null || a.Value == 0)
            return "—";
        double pct = (b.Value - a.Value) / a.Value * 100.0;
        return (pct >= 0 ? "+" : "") + pct.ToString("0.0", Pt) + " %";
    }

    private async Task RunAsync()
    {
        if (IsRunning)
            return;

        var selected = Items.Where(i => i.IsEnabled && i.CanApply).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Nenhuma opção selecionada (ou a tarefa ainda não existe nesta versão).";
            return;
        }

        IsRunning = true;
        try
        {
            StatusText = "A executar micro-benchmark (ANTES) — alguns segundos…";
            Before = await _benchmark.RunAndSaveAsync("quick-before");

            foreach (var item in selected)
            {
                item.State = ApplyState.InProgress;
                StatusText = $"A aplicar: {item.Title}…";

                try
                {
                    var result = await _runOptimization.ExecuteAsync(item.TaskKey);
                    if (result.Success)
                    {
                        item.State = ApplyState.Applied;
                        item.ActionId = result.ActionId;
                    }
                    else if (result.Status == OptimizationStatus.Blocked)
                    {
                        item.State = ApplyState.Blocked;
                    }
                    else
                    {
                        item.State = ApplyState.Failed;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Otimização Rápida: falha em {Key}.", item.TaskKey);
                    item.State = ApplyState.Failed;
                }
            }

            StatusText = "A executar micro-benchmark (DEPOIS) — alguns segundos…";
            After = await _benchmark.RunAndSaveAsync("quick-after");

            int applied = selected.Count(i => i.State == ApplyState.Applied);
            int blocked = selected.Count(i => i.State == ApplyState.Blocked);
            int failed = selected.Count(i => i.State == ApplyState.Failed);
            StatusText = $"Concluído: {applied} aplicado(s), {blocked} bloqueado(s) por elevação, {failed} falha(s).";

            await _notifications.EmitAsync(
                failed == 0 && blocked == 0
                    ? NotificationKind.Success
                    : failed > 0 ? NotificationKind.Error : NotificationKind.Warning,
                "Otimização Rápida concluída",
                StatusText + " Micro-benchmark antes/depois visível no ecrã (medição real, não holística).");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Otimização Rápida falhou.");
            StatusText = "Falha inesperada — veja o histórico e os logs.";
            await _notifications.EmitAsync(NotificationKind.Error, "Otimização Rápida falhou", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RevertAsync(QuickItemRow item)
    {
        if (item.State != ApplyState.Applied || item.ActionId is null)
            return;

        item.State = ApplyState.InProgress;
        try
        {
            await _rollback.RollbackAsync(item.ActionId.Value);
            item.State = ApplyState.NotApplied;
            item.ActionId = null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Revert de {Key} falhou.", item.TaskKey);
            item.State = ApplyState.Applied;
        }
    }
}

/// <summary>Linha de item da Otimização Rápida.</summary>
public sealed class QuickItemRow : ObservableObject
{
    private bool _isEnabled = true;
    private QuickOptimizationViewModel.ApplyState _state = QuickOptimizationViewModel.ApplyState.NotApplied;
    private Guid? _actionId;

    public QuickItemRow(QuickOptimizationSet.QuickItem item, bool canApply)
    {
        TaskKey = item.TaskKey;
        Title = item.Title;
        Description = item.Description;
        Risk = item.Risk;
        RiskText = Risk switch
        {
            RiskLevel.Low => "Baixo",
            RiskLevel.Medium => "Médio",
            _ => "Alto",
        };
        CanApply = canApply;
    }

    public string TaskKey { get; }
    public string Title { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string RiskText { get; }

    /// <summary>false = a tarefa não está registada nesta versão (botão desativado, honestamente).</summary>
    public bool CanApply { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (Set(ref _isEnabled, value))
                OnPropertyChanged(nameof(EffectiveEnabled));
        }
    }

    public bool EffectiveEnabled => IsEnabled && CanApply;

    public QuickOptimizationViewModel.ApplyState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(IsApplied));
                OnPropertyChanged(nameof(IsInProgress));
            }
        }
    }

    public Guid? ActionId
    {
        get => _actionId;
        set
        {
            if (Set(ref _actionId, value))
                OnPropertyChanged(nameof(IsApplied));
        }
    }

    public string? StateText => State switch
    {
        QuickOptimizationViewModel.ApplyState.NotApplied => null,
        QuickOptimizationViewModel.ApplyState.InProgress => "A aplicar…",
        QuickOptimizationViewModel.ApplyState.Applied => "Aplicado",
        QuickOptimizationViewModel.ApplyState.Failed => "Falhou",
        QuickOptimizationViewModel.ApplyState.Blocked => "Bloqueado (requer admin)",
        _ => null,
    };

    public bool IsApplied => State == QuickOptimizationViewModel.ApplyState.Applied && ActionId is not null;

    public bool IsInProgress => State == QuickOptimizationViewModel.ApplyState.InProgress;
}

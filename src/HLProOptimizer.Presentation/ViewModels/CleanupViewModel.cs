using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Presentation.ViewModels.Items;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// Tela "Limpeza": scan por categoria (sistema, aplicativos, registro, lixeira…),
/// seleção granular dos alvos e remoção com confirmação.
/// </summary>
/// <remarks>
/// <para>
/// <b>Seleção tri-estado.</b> Cada grupo tem um <c>CheckBox</c> de cabeçalho que
/// reflete "todos/nenhum/parcial" (<see cref="CleanupGroupViewModel"/>). Os totais
/// (bytes selecionados) são recalculados por evento — nunca por polling.
/// </para>
/// <para>
/// <b>Confirmação configurável.</b> Quando <c>AppSettings.ConfirmBeforeDelete</c>
/// está ativo, a remoção passa por <see cref="IDialogService.ConfirmAsync"/> marcado
/// como destrutivo (botão vermelho), cumprindo o requisito de segurança do produto.
/// </para>
/// </remarks>
public sealed partial class CleanupViewModel : ViewModelBase
{
    private readonly ICleanupService _cleanup;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly IElevationService _elevation;
    private readonly IActionHistoryService _history;

    private CleanupResult? _lastResult;

    /// <summary>Cria o ViewModel da limpeza.</summary>
    /// <param name="cleanup">Serviço de limpeza.</param>
    /// <param name="dialogs">Diálogos.</param>
    /// <param name="settings">Configurações.</param>
    /// <param name="elevation">Elevação sob demanda.</param>
    /// <param name="history">Histórico de ações.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public CleanupViewModel(
        ICleanupService cleanup,
        IDialogService dialogs,
        ISettingsService settings,
        IElevationService elevation,
        IActionHistoryService history,
        ILocalizationService localization,
        ILogger<CleanupViewModel> logger)
        : base(localization, logger)
    {
        _cleanup = cleanup;
        _dialogs = dialogs;
        _settings = settings;
        _elevation = elevation;
        _history = history;

        Level = _settings.Current.OptimizationLevel;
        IsElevated = _elevation.IsElevated;
    }

    /// <summary>Grupos de alvos encontrados pelo scan.</summary>
    public ObservableCollection<CleanupGroupViewModel> Groups { get; } = [];

    /// <summary>Níveis de agressividade disponíveis.</summary>
    public IReadOnlyList<OptimizationLevel> Levels { get; } = Enum.GetValues<OptimizationLevel>();

    /// <summary>Nível de agressividade do scan.</summary>
    [ObservableProperty]
    private OptimizationLevel _level;

    /// <summary>Indica se o scan já foi executado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroups))]
    private bool _isScanned;

    /// <summary>Indica se há grupos com alvos.</summary>
    public bool HasGroups => Groups.Count > 0;

    /// <summary>Total de bytes encontrados.</summary>
    [ObservableProperty]
    private string _totalText = "0 MB";

    /// <summary>Total de bytes selecionados.</summary>
    [ObservableProperty]
    private string _selectedText = "0 MB";

    /// <summary>Quantidade de alvos selecionados.</summary>
    [ObservableProperty]
    private int _selectedTargetCount;

    /// <summary>Indica se há algo selecionado (habilita o botão "Limpar").</summary>
    [ObservableProperty]
    private bool _hasSelection;

    /// <summary>Percentual do scan (barra embutida).</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>Passo atual do scan.</summary>
    [ObservableProperty]
    private string _currentStepText = string.Empty;

    /// <summary>Indica se o processo é administrador.</summary>
    [ObservableProperty]
    private bool _isElevated;

    /// <summary>Arquivos removidos na última limpeza.</summary>
    [ObservableProperty]
    private int _deletedCount;

    /// <summary>Espaço liberado na última limpeza.</summary>
    [ObservableProperty]
    private string _freedText = "0 MB";

    /// <summary>Arquivos ignorados na última limpeza.</summary>
    [ObservableProperty]
    private int _skippedCount;

    /// <summary>Arquivos com falha na última limpeza.</summary>
    [ObservableProperty]
    private int _failedCount;

    /// <summary>Indica se há resultado para exibir.</summary>
    [ObservableProperty]
    private bool _hasResult;

    /// <summary>Resultado da última limpeza (para exportação/re-scan).</summary>
    public CleanupResult? LastResult => _lastResult;

    /// <summary>Executa o scan de limpeza.</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsScanned = false;
        Groups.Clear();
        ProgressPercent = 0;
        HasResult = false;

        IReadOnlyList<CleanupCategoryGroup>? groups = null;

        var completed = await _dialogs.ShowProgressAsync(
            L("Cleanup_Scan"),
            async (progress, cancellationToken) =>
            {
                var forwarding = new Progress<ScanProgress>(p =>
                {
                    ProgressPercent = p.Percent;
                    CurrentStepText = p.CurrentStepName;
                    progress.Report(p);
                });

                groups = await _cleanup.ScanAsync(Level, forwarding, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!completed || groups is null)
        {
            StatusMessage = L("Dlg_CancelledTitle");
            ProgressPercent = 0;
            CurrentStepText = string.Empty;
            return;
        }

        ApplyGroups(groups);

        ProgressPercent = 100;
        CurrentStepText = string.Empty;
        StatusMessage = LF("Cleanup_ScanSummary", SelectedTargetCount, SelectedText);
    }

    /// <summary>Limpa os alvos selecionados.</summary>
    [RelayCommand]
    private async Task CleanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var targets = Groups
            .SelectMany(g => g.Targets)
            .Where(t => t.IsSelected)
            .Select(t => t.Model)
            .ToList();

        if (targets.Count == 0)
        {
            await _dialogs.ShowInfoAsync(L("Cleanup_Clean"), L("Cleanup_NoTargets")).ConfigureAwait(true);
            return;
        }

        if (_settings.Current.ConfirmBeforeDelete)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                L("Cleanup_ConfirmTitle"),
                LF("Cleanup_ConfirmMessage", targets.Count, ByteFormat.Format(targets.Sum(t => t.EstimatedBytes))),
                confirmText: L("Cleanup_Clean"),
                isDestructive: true).ConfigureAwait(true);

            if (!confirmed)
            {
                StatusMessage = L("Dlg_CancelledTitle");
                return;
            }
        }

        CleanupResult? result = null;

        var completed = await _dialogs.ShowProgressAsync(
            L("Cleanup_Clean"),
            async (progress, cancellationToken) =>
            {
                result = await _cleanup.CleanAsync(targets, progress, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!completed || result is null)
        {
            StatusMessage = L("Dlg_CancelledTitle");
            return;
        }

        _lastResult = result;

        ApplyResult(result);

        await _history.RecordCleanupAsync(result).ConfigureAwait(true);

        // Re-scan silencioso: os totais precisam refletir o espaço já liberado.
        await RescanQuietlyAsync().ConfigureAwait(true);
    }

    /// <summary>Limpeza rápida das categorias mais comuns (sem seleção manual).</summary>
    [RelayCommand]
    private async Task QuickCleanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (_settings.Current.ConfirmBeforeDelete)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                L("Cleanup_ConfirmTitle"),
                L("Cleanup_QuickConfirm"),
                confirmText: L("Common_Clean"),
                isDestructive: true).ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }
        }

        CleanupResult? result = null;

        var completed = await _dialogs.ShowProgressAsync(
            L("Cleanup_Clean"),
            async (progress, cancellationToken) =>
            {
                result = await _cleanup.QuickCleanAsync(progress, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!completed || result is null)
        {
            StatusMessage = L("Dlg_CancelledTitle");
            return;
        }

        _lastResult = result;

        ApplyResult(result);

        await _history.RecordCleanupAsync(result).ConfigureAwait(true);
        await RescanQuietlyAsync().ConfigureAwait(true);
    }

    /// <summary>Marca todos os alvos seguros.</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var group in Groups)
        {
            foreach (var target in group.Targets)
            {
                target.IsSelected = true;
            }
        }
    }

    /// <summary>Marca apenas os alvos considerados seguros.</summary>
    [RelayCommand]
    private void SelectSafeOnly()
    {
        foreach (var group in Groups)
        {
            foreach (var target in group.Targets)
            {
                target.IsSelected = target.IsSafe && (!target.RequiresAdmin || IsElevated);
            }
        }
    }

    /// <summary>Desmarca tudo.</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var group in Groups)
        {
            foreach (var target in group.Targets)
            {
                target.IsSelected = false;
            }
        }
    }

    /// <summary>Abre o Gerenciador de Arquivos na pasta do alvo selecionado.</summary>
    /// <param name="target">Alvo.</param>
    [RelayCommand]
    private async Task OpenTargetLocationAsync(CleanupTargetViewModel? target)
    {
        if (target?.Model.Paths.FirstOrDefault() is not { } path)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                StatusMessage = L("Cleanup_LocationUnavailable");
                return;
            }

            await Task.Run(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            })).ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Reinicia o aplicativo como administrador para limpar áreas protegidas.</summary>
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
    // Internos
    // ---------------------------------------------------------------------

    private void ApplyGroups(IReadOnlyList<CleanupCategoryGroup> groups)
    {
        Groups.Clear();

        foreach (var group in groups.Where(g => g.Targets.Count > 0).OrderByDescending(g => g.TotalBytes))
        {
            var viewModel = new CleanupGroupViewModel(group);
            viewModel.SelectionChanged += (_, _) => RecalculateTotals();
            Groups.Add(viewModel);
        }

        IsScanned = true;

        RecalculateTotals();

        OnPropertyChanged(nameof(HasGroups));
    }

    private void RecalculateTotals()
    {
        var total = Groups.Sum(g => g.TotalBytes);
        var selected = Groups.Sum(g => g.SelectedBytes);
        var count = Groups.Sum(g => g.SelectedCount);

        TotalText = ByteFormat.Format(total);
        SelectedText = ByteFormat.Format(selected);
        SelectedTargetCount = count;
        HasSelection = count > 0;
    }

    private void ApplyResult(CleanupResult result)
    {
        HasResult = true;
        DeletedCount = result.DeletedFileCount;
        FreedText = result.FreedFormatted;
        SkippedCount = result.SkippedFileCount;
        FailedCount = result.FailedFileCount;

        StatusMessage = result.HasFailures
            ? LF("Cleanup_ResultWithFailures", result.DeletedFileCount, result.FreedFormatted, result.FailedFileCount)
            : LF("Cleanup_ResultSummary", result.DeletedFileCount, result.FreedFormatted);
    }

    /// <summary>Refaz o scan sem abrir diálogo (mantém os totais coerentes).</summary>
    private async Task RescanQuietlyAsync()
    {
        try
        {
            var groups = await _cleanup.ScanAsync(Level).ConfigureAwait(true);

            OnUiThread(() => ApplyGroups(groups));
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Re-scan silencioso da limpeza falhou.");
        }
    }
}

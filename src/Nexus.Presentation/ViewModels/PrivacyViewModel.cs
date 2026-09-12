using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexus.Application.Recommendations;
using Nexus.Application.UseCases;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Presentation.ViewModels;

/// <summary>
/// Privacidade / Telemetria (spec §4.7): toggles REAIS sobre chaves de
/// registry e serviços conhecidos — cada um documentado (o que faz, porquê
/// é seguro, como reverte). O estado mostrado vem de probes reais
/// (registry/WMI): N/D quando a fonte falha, nunca um estado inventado.
/// </summary>
public sealed class PrivacyViewModel : ObservableObject
{
    private readonly IRegistryAccess _registry;
    private readonly IServiceInspector _services;
    private readonly RunOptimizationUseCase _runOptimization;
    private readonly RollbackUseCase _rollback;
    private readonly IHistoryStore _history;
    private readonly INotificationHub _notifications;
    private readonly IElevationService _elevation;
    private readonly ILogger<PrivacyViewModel> _log;

    private string _statusText = "A ler estado real (registry/WMI)…";

    public PrivacyViewModel(
        IRegistryAccess registry,
        IServiceInspector services,
        RunOptimizationUseCase runOptimization,
        RollbackUseCase rollback,
        IHistoryStore history,
        INotificationHub notifications,
        IElevationService elevation,
        ILogger<PrivacyViewModel> log)
    {
        _registry = registry;
        _services = services;
        _runOptimization = runOptimization;
        _rollback = rollback;
        _history = history;
        _notifications = notifications;
        _elevation = elevation;
        _log = log;

        IsElevated = elevation.IsElevated;
        Rows = new ObservableCollection<PrivacyRow>(new[]
        {
            new PrivacyRow(
                TaskKeys.TelemetryDisable,
                "Telemetria de base (Windows)",
                "Política AllowTelemetry=0 em HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection. " +
                "Reduz a recolha de dados de diagnóstico (CEIP). Não afeta Windows Update, segurança nem diagnóstico de rede. " +
                "Requer administrador. Reversível.",
                requiresElevation: true),

            new PrivacyRow(
                TaskKeys.AdvertisingIdDisable,
                "ID de anúncio (publicidade personalizada)",
                "AdvertisingInfo\\Enabled=0 em HKCU (conta atual). As apps deixam de usar o ID de anúncio para " +
                "publicidade personalizada. Não requer administrador. Reversível.",
                requiresElevation: false),

            new PrivacyRow(
                TaskKeys.ServiceDiagTrackDisable,
                "Serviço de telemetria (DiagTrack)",
                "Põe 'Connected User Experiences and Telemetry' em disabled — o serviço que envia dados de " +
                "diagnóstico/telemetria. Requer administrador. Reversível por rollback.",
                requiresElevation: true),
        });

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ApplyCommand = new AsyncRelayCommand<PrivacyRow>(ApplyAsync);
        RevertCommand = new AsyncRelayCommand<PrivacyRow>(RevertAsync);

        _ = RefreshAsync();
    }

    public ObservableCollection<PrivacyRow> Rows { get; }

    public bool IsElevated { get; }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<PrivacyRow> ApplyCommand { get; }
    public IAsyncRelayCommand<PrivacyRow> RevertCommand { get; }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public async Task RefreshAsync()
    {
        StatusText = "A ler estado real (registry/WMI)…";
        try
        {
            var telemetry = await ReadTelemetryStateAsync();
            var adId = await ReadAdIdStateAsync();
            var diagTrack = await ReadDiagTrackStateAsync();

            foreach (var row in Rows)
            {
                row.SetState(
                    row.TaskKey switch
                    {
                        TaskKeys.TelemetryDisable => telemetry,
                        TaskKeys.AdvertisingIdDisable => adId,
                        TaskKeys.ServiceDiagTrackDisable => diagTrack,
                        _ => new ToggleState("N/D", known: false, disabled: false),
                    });
            }

            StatusText = "Estado lido de fontes reais (registry HKLM/HKCU e WMI).";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha ao ler o estado de privacidade.");
            StatusText = "Falha ao ler o estado — os toggles mostram N/D.";
            foreach (var row in Rows)
                row.SetState(new ToggleState("N/D", known: false, disabled: false));
        }
    }

    private async Task<ToggleState> ReadTelemetryStateAsync()
    {
        try
        {
            var probe = await _registry.ProbeAsync(
                @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
            if (!probe.Exists)
                return new ToggleState("Ativa (predefinição do Windows — sem política definida)", true, false);
            return (int)(probe.Value ?? 1) == 0
                ? new ToggleState("Desativada (política AllowTelemetry=0)", true, true)
                : new ToggleState($"Ativa (política AllowTelemetry={(int)(probe.Value ?? 1)})", true, false);
        }
        catch
        {
            return new ToggleState("N/D", false, false);
        }
    }

    private async Task<ToggleState> ReadAdIdStateAsync()
    {
        try
        {
            var probe = await _registry.ProbeAsync(
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled");
            if (!probe.Exists)
                return new ToggleState("Ativo (predefinição — sem valor definido)", true, false);
            return (int)(probe.Value ?? 1) == 0
                ? new ToggleState("Desativado", true, true)
                : new ToggleState("Ativo", true, false);
        }
        catch
        {
            return new ToggleState("N/D", false, false);
        }
    }

    private async Task<ToggleState> ReadDiagTrackStateAsync()
    {
        try
        {
            var probe = await _services.ProbeServiceAsync("DiagTrack");
            if (probe is null || !probe.Exists)
                return new ToggleState("N/D (serviço não encontrado)", true, false);
            return string.Equals(probe.StartMode, "Disabled", StringComparison.OrdinalIgnoreCase)
                ? new ToggleState($"Desativado (arranque; estado: {probe.State})", true, true)
                : new ToggleState($"Ativo (arranque: {probe.StartMode}; estado: {probe.State})", true, false);
        }
        catch
        {
            return new ToggleState("N/D", false, false);
        }
    }

    private async Task ApplyAsync(PrivacyRow row)
    {
        row.IsBusy = true;
        try
        {
            var result = await _runOptimization.ExecuteAsync(row.TaskKey);
            if (!result.Success)
            {
                row.SetState(new ToggleState("N/D", false, false));
                await RefreshAsync();
            }
            else
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Aplicação do toggle {Key} falhou.", row.TaskKey);
            await _notifications.EmitAsync(NotificationKind.Error, "Falha ao aplicar o toggle", ex.Message);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private async Task RevertAsync(PrivacyRow row)
    {
        row.IsBusy = true;
        try
        {
            var actions = await _history.QueryAsync(take: 100);
            var latest = actions.FirstOrDefault(a =>
                string.Equals(a.TaskKey, row.TaskKey, StringComparison.OrdinalIgnoreCase)
                && a.BackupIds.Count > 0
                && a.Status == OptimizationStatus.Success);

            if (latest is null)
            {
                await _notifications.EmitAsync(NotificationKind.Info, "Nada para reverter",
                    $"Não há backups de '{row.Title}' no histórico (a alteração ainda não foi aplicada ou o rollback já foi feito).");
                return;
            }

            await _rollback.RollbackAsync(latest.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Revert do toggle {Key} falhou.", row.TaskKey);
            await _notifications.EmitAsync(NotificationKind.Error, "Falha no revert", ex.Message);
        }
        finally
        {
            row.IsBusy = false;
        }
    }
}

public sealed record ToggleState(string Text, bool Known, bool Disabled);

/// <summary>Toggle de privacidade (estado real + ações).</summary>
public sealed class PrivacyRow : ObservableObject
{
    private string _stateText = "N/D";
    private bool _stateKnown;
    private bool _stateDisabled;
    private bool _isBusy;

    public PrivacyRow(string taskKey, string title, string description, bool requiresElevation)
    {
        TaskKey = taskKey;
        Title = title;
        Description = description;
        RequiresElevation = requiresElevation;
    }

    public string TaskKey { get; }
    public string Title { get; }
    public string Description { get; }
    public bool RequiresElevation { get; }

    public string StateText
    {
        get => _stateText;
        private set => Set(ref _stateText, value);
    }

    public bool StateKnown
    {
        get => _stateKnown;
        private set => Set(ref _stateKnown, value);
    }

    public bool StateDisabled
    {
        get => _stateDisabled;
        private set => Set(ref _stateDisabled, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
                OnPropertyChanged(nameof(ApplyButtonText));
        }
    }

    public string ApplyButtonText => IsBusy ? "A aplicar…" : "Desativar";

    /// <summary>Aplicar só faz sentido enquanto o recurso não está desativado (reverter → "Reverter").</summary>
    public bool ApplyEnabled => !IsBusy && !(StateKnown && StateDisabled);

    public void SetState(ToggleState state)
    {
        StateText = state.Text;
        StateKnown = state.Known;
        StateDisabled = state.Disabled;
        OnPropertyChanged(nameof(ApplyEnabled));
    }
}

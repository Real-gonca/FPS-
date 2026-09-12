using Microsoft.Extensions.Logging;
using Nexus.Application.Optimization;
using Nexus.Application.Profiles;
using Nexus.Application.Tasks;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application.UseCases;

/// <summary>
/// Casos de uso de serviços (spec §4.6):
/// - operação individual (start/stop/alterar start mode) com gatekeeping de
///   elevação + pipeline completo (cada operação fica no histórico com
///   rollback individual);
/// - aplicação de PERFIL: as alterações correm uma a uma pelo mesmo pipeline
///   (atómicas e individualmente reversíveis); se uma falhar, as restantes
///   não correm (o perfil não fica "meio aplicado" em silêncio).
/// </summary>
public sealed class ServiceOperationUseCase
{
    private readonly IElevationService _elevation;
    private readonly OptimizationOrchestrator _orchestrator;
    private readonly INotificationHub _notifications;
    private readonly ISystemCommandExecutor _executor;
    private readonly ILogger<ServiceOperationUseCase> _log;

    public ServiceOperationUseCase(
        IElevationService elevation,
        OptimizationOrchestrator orchestrator,
        INotificationHub notifications,
        ISystemCommandExecutor executor,
        ILogger<ServiceOperationUseCase> log)
    {
        _elevation = elevation;
        _orchestrator = orchestrator;
        _notifications = notifications;
        _executor = executor;
        _log = log;
    }

    public async Task<OptimizationResult> ChangeStartModeAsync(
        string serviceKey, string displayName, string desiredStartMode, string justification,
        CancellationToken ct = default)
    {
        if (!_elevation.IsElevated)
            return await BlockedAsync();

        var task = new ServiceTask(
            key: $"op-{serviceKey}-{desiredStartMode}",
            serviceKey: serviceKey,
            displayName: displayName,
            operation: ServiceOperation.ChangeStartMode,
            desiredStartMode: desiredStartMode,
            justification: justification,
            executor: _executor,
            risk: RiskLevel.Low);

        return await _orchestrator.ExecuteAsync(task, ct);
    }

    public async Task<OptimizationResult> StartServiceAsync(
        string serviceKey, string displayName, string justification, CancellationToken ct = default)
    {
        if (!_elevation.IsElevated)
            return await BlockedAsync();

        var task = new ServiceTask(
            key: $"op-{serviceKey}-start",
            serviceKey: serviceKey,
            displayName: displayName,
            operation: ServiceOperation.Start,
            desiredStartMode: null,
            justification: justification,
            executor: _executor);

        return await _orchestrator.ExecuteAsync(task, ct);
    }

    public async Task<OptimizationResult> StopServiceAsync(
        string serviceKey, string displayName, string justification, CancellationToken ct = default)
    {
        if (!_elevation.IsElevated)
            return await BlockedAsync();

        var task = new ServiceTask(
            key: $"op-{serviceKey}-stop",
            serviceKey: serviceKey,
            displayName: displayName,
            operation: ServiceOperation.Stop,
            desiredStartMode: null,
            justification: justification,
            executor: _executor);

        return await _orchestrator.ExecuteAsync(task, ct);
    }

    /// <summary>
    /// Aplica um perfil, alteração a alteração. Devolve o resumo; os
    /// detalhes (incluindo actionIds para rollback) ficam no histórico.
    /// </summary>
    public async Task<ProfileApplyOutcome> ApplyProfileAsync(ServiceProfile profile, CancellationToken ct = default)
    {
        if (!_elevation.IsElevated)
        {
            await _notifications.EmitAsync(NotificationKind.Warning, "Requer administrador",
                $"O perfil {profile.Name} altera serviços de sistema. Use 'Executar como Admin' no cabeçalho.");
            return new ProfileApplyOutcome(profile, 0, 0, profile.Changes.Count, false,
                "Requer privilégios de administrador (UAC).", new List<Guid>());
        }

        int applied = 0;
        int index = 0;
        var appliedIds = new List<Guid>();
        string? failureMessage = null;
        bool interrupted = false;

        foreach (var change in profile.Changes)
        {
            index++;

            var task = new ServiceTask(
                key: profile.KeyFor(change.ServiceKey),
                serviceKey: change.ServiceKey,
                displayName: change.DisplayName,
                operation: ServiceOperation.ChangeStartMode,
                desiredStartMode: change.TargetStartMode,
                justification: change.Justification,
                executor: _executor,
                risk: profile.Risk);

            OptimizationResult result;
            try
            {
                result = await _orchestrator.ExecuteAsync(task, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result.Success)
            {
                applied++;
                if (result.ActionId is { } id)
                    appliedIds.Add(id);
            }
            else
            {
                interrupted = true;
                failureMessage = result.Message;
                _log.LogWarning("Perfil {Profile}: alteração {Service} falhou — as restantes não serão aplicadas.",
                    profile.Key, change.ServiceKey);
                break;
            }
        }

        int skipped = profile.Changes.Count - index;
        string summary = interrupted
            ? $"Perfil interrompido: {applied} aplicado(s), falha na alteração {index} de {profile.Changes.Count}, {skipped} não executado(s). " +
              $"Erro: {failureMessage} Os já aplicados podem ser revertidos individualmente."
            : skipped > 0
                ? $"Perfil cancelado pelo utilizador: {applied} aplicado(s), {skipped} não executado(s)."
                : $"Perfil aplicado: {applied} serviço(s) alterado(s). Cada um tem rollback individual.";

        var kind = interrupted ? NotificationKind.Error
                   : skipped > 0 ? NotificationKind.Warning
                   : NotificationKind.Success;
        await _notifications.EmitAsync(kind, interrupted
                ? "Perfil de serviços incompleto"
                : skipped > 0 ? "Perfil de serviços cancelado" : "Perfil de serviços aplicado",
            $"{profile.Name} — {summary}");

        return new ProfileApplyOutcome(profile, applied, interrupted ? 1 : 0, skipped,
            !interrupted && skipped == 0, summary, appliedIds);
    }

    private async Task<OptimizationResult> BlockedAsync()
    {
        await _notifications.EmitAsync(NotificationKind.Warning, "Requer administrador",
            "As alterações de serviços exigem privilégios de administrador. Use 'Executar como Admin' no cabeçalho.");
        return OptimizationResult.Blocked("Requer privilégios de administrador (UAC).");
    }
}

/// <summary>Resumo da aplicação de um perfil (a UI mostra o estado real).</summary>
public sealed record ProfileApplyOutcome(
    ServiceProfile Profile,
    int Applied,
    int Failed,
    int Skipped,
    bool FullyApplied,
    string Summary,
    IReadOnlyList<Guid> AppliedActionIds);

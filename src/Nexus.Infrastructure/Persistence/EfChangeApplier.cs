using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Persistence;

/// <summary>
/// Backup/rollback por tipo de alteração (pipeline da spec §2.2).
///
/// Registry (Patch 1): probe completo ANTES da escrita — o estado anterior
/// (valor E tipo, ou a ausência do valor) fica em BackupRow.PayloadJson.
///
/// Service (Patch 2): probe WMI ANTES da escrita — start mode + estado
/// atuais ficam no payload; o restauro re-aplica o start mode via
/// sc.exe (whitelist) e realinha o estado (start/stop) se necessário.
///
/// File: planeado no patch 4 (Limpeza Avançada).
/// </summary>
public sealed class EfChangeApplier : ScopedDbAccess, IChangeApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly IRegistryAccess _registry;
    private readonly IServiceInspector _services;
    private readonly ISystemCommandExecutor _executor;

    public EfChangeApplier(
        IServiceScopeFactory scopes,
        ILogger<EfChangeApplier> log,
        IRegistryAccess registry,
        IServiceInspector services,
        ISystemCommandExecutor executor)
        : base(scopes, log)
    {
        _registry = registry;
        _services = services;
        _executor = executor;
    }

    public async Task<IReadOnlyList<Guid>> BackupAsync(IEnumerable<ChangeDescriptor> changes, CancellationToken ct = default)
    {
        var ids = new List<Guid>();

        foreach (var change in changes)
        {
            string payload = change.Kind switch
            {
                ChangeKind.Registry => await BackupRegistryAsync(change, ct),
                ChangeKind.Service => await BackupServiceAsync(change, ct),
                _ => throw new NotSupportedException(
                    $"Backup do tipo '{change.Kind}' não está implementado neste patch " +
                    "(roadmap: docs/patches). A tarefa é rejeitada ANTES de aplicar qualquer alteração."),
            };

            Guid id = Guid.NewGuid();
            await WithDbAsync(async db =>
            {
                db.Backups.Add(new BackupRow
                {
                    Id = id,
                    Kind = change.Kind.ToString(),
                    Target = change.Target,
                    Detail = change.Detail,
                    Description = change.Description,
                    PayloadJson = payload,
                    CreatedUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(ct);
            }, ct);
            ids.Add(id);
        }

        Log.LogInformation("Backups criados: {Count}.", ids.Count);
        return ids;
    }

    public async Task RestoreAsync(IEnumerable<Guid> backupIds, CancellationToken ct = default)
    {
        int restored = 0;

        // Ordem inversa da criação: a última alteração volta primeiro.
        foreach (var id in backupIds.Reverse())
        {
            var row = await WithDbAsync(
                async db => await db.Backups.FindAsync(new object[] { id }, ct), ct);
            if (row is null)
                throw new InvalidOperationException($"Backup {id} não encontrado na base de dados.");

            string kind = row.Kind;
            string target = row.Target;
            string detail = row.Detail;
            string payloadJson = row.PayloadJson;

            if (kind == nameof(ChangeKind.Registry))
            {
                var payload = JsonSerializer.Deserialize<RegistryBackupPayload>(payloadJson, JsonOptions)
                    ?? throw new InvalidOperationException($"Backup {id}: payload inválido.");

                if (!payload.Exists)
                {
                    await _registry.DeleteAsync(target, detail, ct);
                }
                else
                {
                    await _registry.SetAsync(target, detail, payload.Kind ?? RegistryValueKind.String,
                        ConvertValue(payload), ct);
                }
            }
            else if (kind == nameof(ChangeKind.Service))
            {
                var payload = JsonSerializer.Deserialize<ServiceBackupPayload>(payloadJson, JsonOptions)
                    ?? throw new InvalidOperationException($"Backup {id}: payload inválido.");

                await RestoreServiceAsync(payload, ct);
            }
            else
            {
                throw new NotSupportedException($"Restauro do tipo '{kind}' não está implementado neste patch.");
            }

            var capturedRow = row;
            await WithDbAsync(async db =>
            {
                capturedRow.RestoredUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }, ct);
            restored++;
        }

        Log.LogInformation("Rollback concluído: {Count} backup(s) restaurado(s).", restored);
    }

    private async Task<string> BackupRegistryAsync(ChangeDescriptor change, CancellationToken ct)
    {
        var probe = await _registry.ProbeAsync(change.Target, change.Detail, ct);

        return JsonSerializer.Serialize(new RegistryBackupPayload
        {
            Exists = probe.Exists,
            Kind = probe.Kind,
            ValueType = probe.Value switch
            {
                _ when probe.Kind == RegistryValueKind.Dword => "dword",
                _ when probe.Kind == RegistryValueKind.Qword => "qword",
                _ when probe.Kind == RegistryValueKind.MultiString => "multistring",
                _ when probe.Kind == RegistryValueKind.Binary => "binary",
                _ => probe.Exists ? "string" : null,
            },
            ValueJson = probe.Value switch
            {
                int i => i.ToString(),
                long l => l.ToString(),
                string s => s,
                string[] multi => JsonSerializer.Serialize(multi),
                byte[] binary => Convert.ToBase64String(binary),
                null => null,
                other => other.ToString(),
            },
        }, JsonOptions);
    }

    private static object ConvertValue(RegistryBackupPayload payload) => payload.ValueType switch
    {
        "dword" => int.Parse(payload.ValueJson ?? "0"),
        "qword" => long.Parse(payload.ValueJson ?? "0"),
        "multistring" => JsonSerializer.Deserialize<string[]>(payload.ValueJson ?? "[]") ?? Array.Empty<string>(),
        "binary" => Convert.FromBase64String(payload.ValueJson ?? string.Empty),
        _ => payload.ValueJson,
    };

    // ── Serviços (Patch 2) ─────────────────────────────────────────────────

    private async Task<string> BackupServiceAsync(ChangeDescriptor change, CancellationToken ct)
    {
        var probe = await _services.ProbeServiceAsync(change.Target, ct)
            ?? throw new InvalidOperationException(
                $"Não foi possível ler o serviço '{change.Target}' (WMI indisponível?). A alteração foi abortada antes de aplicar.");
        if (!probe.Exists)
            throw new InvalidOperationException($"O serviço '{change.Target}' não existe neste sistema. A alteração foi abortada.");

        return JsonSerializer.Serialize(new ServiceBackupPayload
        {
            Name = probe.Name,
            DisplayName = probe.DisplayName,
            StartMode = probe.StartMode,
            State = probe.State,
        }, JsonOptions);
    }

    private async Task RestoreServiceAsync(ServiceBackupPayload payload, CancellationToken ct)
    {
        // 1) Devolver o start mode (se for definível via sc — Boot/System não são).
        string? scMode = payload.StartMode switch
        {
            "Auto" => "auto",
            "Manual" => "demand",
            "Demand" => "demand",
            "Disabled" => "disabled",
            _ => null, // Boot/System/Unknown → não definível via sc; log e seguir
        };
        if (scMode is not null)
        {
            var changeResult = await _executor.RunAsync(
                new CommandSpec("sc.exe", $"change {payload.Name} start={scMode}"), ct);
            if (!changeResult.WasWhitelisted)
                throw new InvalidOperationException($"Restauro de '{payload.Name}': {changeResult.RejectionReason}");
            if (!changeResult.Succeeded)
                throw new InvalidOperationException($"Restauro do start mode de '{payload.Name}' falhou (exit {changeResult.ExitCode}): {changeResult.StandardError}");
        }
        else
        {
            Log.LogWarning("Restauro de {Name}: start mode anterior ({Mode}) não é definível via sc.exe — apenas o estado foi realinhado.",
                payload.Name, payload.StartMode);
        }

        // 2) Realinhar o estado (Running ↔ Stopped) se divergir do anterior.
        var current = await _services.ProbeServiceAsync(payload.Name, ct);
        if (current is null)
            return; // WMI ficou indisponível a meio do restauro — o start mode já foi devolvido

        bool wasRunning = string.Equals(payload.State, "Running", StringComparison.OrdinalIgnoreCase);
        bool isRunning = string.Equals(current.State, "Running", StringComparison.OrdinalIgnoreCase);
        if (wasRunning && !isRunning)
        {
            var r = await _executor.RunAsync(new CommandSpec("sc.exe", $"start {payload.Name}"), ct);
            if (!r.WasWhitelisted)
                throw new InvalidOperationException($"Restauro de '{payload.Name}': {r.RejectionReason}");
            // 1056 = já estava em execução (idempotência) — tratado acima; erros reais lançam.
            if (!r.Succeeded && r.ExitCode != 1056)
                throw new InvalidOperationException($"Restauro do estado de '{payload.Name}' falhou (exit {r.ExitCode}): {r.StandardError}");
        }
        else if (!wasRunning && isRunning)
        {
            var r = await _executor.RunAsync(new CommandSpec("sc.exe", $"stop {payload.Name}"), ct);
            if (!r.WasWhitelisted)
                throw new InvalidOperationException($"Restauro de '{payload.Name}': {r.RejectionReason}");
            if (!r.Succeeded && r.ExitCode != 1062)
                throw new InvalidOperationException($"Restauro do estado de '{payload.Name}' falhou (exit {r.ExitCode}): {r.StandardError}");
        }
    }
}

internal sealed class ServiceBackupPayload
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? StartMode { get; set; }
    public string? State { get; set; }
}

internal sealed class RegistryBackupPayload
{
    public bool Exists { get; set; }
    public RegistryValueKind? Kind { get; set; }
    public string? ValueType { get; set; }
    public string? ValueJson { get; set; }
}

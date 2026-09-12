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
/// (valor E tipo, ou a ausência do valor) fica em BackupRow.PayloadJson. O
/// restauro re-aplica exatamente esse estado (incluindo "valor não existia").
///
/// Service/File: planeados nos patches 2 e 4. Até lá, BackupAsync LANÇA para
/// esses tipos — ou seja, nenhuma tarefa sem backup suportado chega a
/// escrever algo (a reversibilidade total nunca fica em risco).
/// </summary>
public sealed class EfChangeApplier : ScopedDbAccess, IChangeApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly IRegistryAccess _registry;

    public EfChangeApplier(IServiceScopeFactory scopes, ILogger<EfChangeApplier> log, IRegistryAccess registry)
        : base(scopes, log)
    {
        _registry = registry;
    }

    public async Task<IReadOnlyList<Guid>> BackupAsync(IEnumerable<ChangeDescriptor> changes, CancellationToken ct = default)
    {
        var ids = new List<Guid>();

        foreach (var change in changes)
        {
            string payload = change.Kind switch
            {
                ChangeKind.Registry => await BackupRegistryAsync(change, ct),
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
}

internal sealed class RegistryBackupPayload
{
    public bool Exists { get; set; }
    public RegistryValueKind? Kind { get; set; }
    public string? ValueType { get; set; }
    public string? ValueJson { get; set; }
}

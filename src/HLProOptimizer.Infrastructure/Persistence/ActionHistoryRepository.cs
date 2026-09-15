using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Persistence;

/// <summary>
/// Repositório do histórico de ações (EF Core + SQLite).
/// </summary>
/// <remarks>
/// <para>
/// Cada operação cria um contexto pela fábrica (<see cref="IDbContextFactory{TContext}"/>)
/// porque o histórico é gravado a partir de várias telas simultaneamente.
/// </para>
/// <para>
/// <b>Falhas de banco não derrubam o aplicativo.</b> Um SQLite bloqueado por
/// antivírus ou um disco cheio não pode impedir uma otimização de terminar: os
/// erros são registrados e a operação retorna vazia/falsa.
/// </para>
/// </remarks>
public sealed class ActionHistoryRepository : IActionHistoryRepository
{
    /// <summary>Limite de registros mantidos no histórico (evita crescimento infinito).</summary>
    private const int MaxRetainedRecords = 2000;

    private readonly IDbContextFactory<HlOptimizerDbContext> _factory;
    private readonly ILogger<ActionHistoryRepository> _logger;

    /// <summary>Cria o repositório.</summary>
    /// <param name="factory">Fábrica de contextos.</param>
    /// <param name="logger">Logger.</param>
    public ActionHistoryRepository(IDbContextFactory<HlOptimizerDbContext> factory, ILogger<ActionHistoryRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AddAsync(ActionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            context.ActionRecords.Add(ToEntity(record));

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await PruneAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar a ação '{Title}' no histórico.", record.Title);
        }
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<ActionRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var list = records.ToList();

        if (list.Count == 0)
        {
            return;
        }

        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            context.ActionRecords.AddRange(list.Select(ToEntity));

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await PruneAsync(context, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("{Count} ação(ões) registrada(s) no histórico.", list.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar {Count} ações no histórico.", list.Count);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActionRecord>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, 500);

        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var entities = await context.ActionRecords
                .AsNoTracking()
                .OrderByDescending(e => e.Timestamp)
                .Take(take)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return entities.Select(ToModel).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar as ações recentes.");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActionRecord>> GetByKindAsync(ActionKind kind, int count = 50, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, 500);

        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var entities = await context.ActionRecords
                .AsNoTracking()
                .Where(e => e.Kind == (int)kind)
                .OrderByDescending(e => e.Timestamp)
                .Take(take)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return entities.Select(ToModel).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar as ações do tipo {Kind}.", kind);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            return await context.ActionRecords.CountAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao contar as ações do histórico.");
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            // ExecuteDelete evita carregar milhares de entidades na memória.
            var deleted = await context.ActionRecords.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Histórico de ações limpo ({Count} registro(s) removido(s)).", deleted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao limpar o histórico de ações.");
        }
    }

    /// <summary>Descarta os registros mais antigos além do limite de retenção.</summary>
    private async Task PruneAsync(HlOptimizerDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            var total = await context.ActionRecords.CountAsync(cancellationToken).ConfigureAwait(false);

            if (total <= MaxRetainedRecords)
            {
                return;
            }

            var cutoff = await context.ActionRecords
                .OrderByDescending(e => e.Timestamp)
                .Skip(MaxRetainedRecords)
                .Select(e => e.Timestamp)
                .FirstAsync(cancellationToken)
                .ConfigureAwait(false);

            var deleted = await context.ActionRecords
                .Where(e => e.Timestamp < cutoff)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Histórico podado: {Count} registro(s) antigo(s) removido(s).", deleted);
        }
        catch (Exception ex)
        {
            // Poda é manutenção: falhar não invalida a gravação anterior.
            _logger.LogDebug(ex, "Falha ao podar o histórico de ações.");
        }
    }

    private static ActionRecordEntity ToEntity(ActionRecord record) => new()
    {
        Id = record.Id,
        Timestamp = record.Timestamp,
        Kind = (int)record.Kind,
        Title = record.Title,
        Details = record.Details,
        BytesAffected = record.BytesAffected,
        DurationMs = record.DurationMs,
        Success = record.Success,
        PayloadJson = record.PayloadJson
    };

    private static ActionRecord ToModel(ActionRecordEntity entity) => new()
    {
        Id = entity.Id,
        Timestamp = entity.Timestamp,
        Kind = (ActionKind)entity.Kind,
        Title = entity.Title,
        Details = entity.Details,
        BytesAffected = entity.BytesAffected,
        DurationMs = entity.DurationMs,
        Success = entity.Success,
        PayloadJson = entity.PayloadJson
    };
}

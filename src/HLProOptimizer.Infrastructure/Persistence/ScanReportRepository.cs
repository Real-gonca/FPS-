using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HLProOptimizer.Infrastructure.Persistence;

/// <summary>
/// Repositório de relatórios de análise (EF Core + SQLite).
/// </summary>
/// <remarks>
/// Os problemas do relatório são serializados em JSON com Newtonsoft (padrão do
/// projeto). A serialização usa <see cref="TypeNameHandling.None"/> e preserva
/// enums como texto para que o arquivo .db continue legível em inspeção manual.
/// </remarks>
public sealed class ScanReportRepository : IScanReportRepository
{
    /// <summary>Quantidade máxima de relatórios mantidos.</summary>
    private const int MaxRetainedReports = 50;

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        // Enums legíveis no banco (facilita diagnóstico sem ferramenta externa).
        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
        NullValueHandling = NullValueHandling.Ignore,
        TypeNameHandling = TypeNameHandling.None
    };

    private readonly IDbContextFactory<HlOptimizerDbContext> _factory;
    private readonly ILogger<ScanReportRepository> _logger;

    /// <summary>Cria o repositório.</summary>
    /// <param name="factory">Fábrica de contextos.</param>
    /// <param name="logger">Logger.</param>
    public ScanReportRepository(IDbContextFactory<HlOptimizerDbContext> factory, ILogger<ScanReportRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveAsync(ScanReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var entity = new ScanReportEntity
            {
                Id = report.Id,
                StartedAt = report.StartedAt,
                CompletedAt = report.CompletedAt,
                IssuesJson = JsonConvert.SerializeObject(report.Issues, SerializerSettings),
                FailedCategories = string.Join(';', report.FailedCategories),
                TotalRecoverableBytes = report.TotalRecoverableBytes
            };

            // Reanálise do mesmo relatório sobrescreve (Id é o GUID do scan).
            var existing = await context.ScanReports.FindAsync([entity.Id], cancellationToken).ConfigureAwait(false);

            if (existing is null)
            {
                context.ScanReports.Add(entity);
            }
            else
            {
                existing.StartedAt = entity.StartedAt;
                existing.CompletedAt = entity.CompletedAt;
                existing.IssuesJson = entity.IssuesJson;
                existing.FailedCategories = entity.FailedCategories;
                existing.TotalRecoverableBytes = entity.TotalRecoverableBytes;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await PruneAsync(context, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Relatório {Id} salvo ({Issues} problema(s), {Bytes} recuperáveis).",
                report.Id,
                report.Issues.Count,
                ByteFormat.Format(report.TotalRecoverableBytes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao salvar o relatório {Id}.", report.Id);
        }
    }

    /// <inheritdoc />
    public async Task<ScanReport?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var entity = await context.ScanReports
                .AsNoTracking()
                .OrderByDescending(e => e.StartedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return entity is null ? null : ToModel(entity);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar o último relatório.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ScanReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var entity = await context.ScanReports
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);

            return entity is null ? null : ToModel(entity);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar o relatório {Id}.", id);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScanReport>> GetHistoryAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, MaxRetainedReports);

        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var entities = await context.ScanReports
                .AsNoTracking()
                .OrderByDescending(e => e.StartedAt)
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
            _logger.LogError(ex, "Falha ao consultar o histórico de relatórios.");
            return [];
        }
    }

    /// <summary>Mantém apenas os relatórios mais recentes.</summary>
    private async Task PruneAsync(HlOptimizerDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = await context.ScanReports
                .OrderByDescending(e => e.StartedAt)
                .Skip(MaxRetainedReports)
                .Select(e => e.StartedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (cutoff == default)
            {
                return;
            }

            var deleted = await context.ScanReports
                .Where(e => e.StartedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.LogInformation("{Count} relatório(s) antigo(s) removido(s).", deleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao podar os relatórios antigos.");
        }
    }

    private ScanReport ToModel(ScanReportEntity entity)
    {
        IReadOnlyList<AnalysisIssue> issues;

        try
        {
            issues = JsonConvert.DeserializeObject<List<AnalysisIssue>>(entity.IssuesJson, SerializerSettings) ?? [];
        }
        catch (JsonException ex)
        {
            // JSON de uma versão antiga: o relatório continua útil (datas/totais).
            issues = [];
            _logger.LogWarning(ex, "Problemas do relatório {Id} ilegíveis; exibindo apenas o resumo.", entity.Id);
        }

        return new ScanReport
        {
            Id = entity.Id,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt,
            Issues = issues,
            FailedCategories = string.IsNullOrWhiteSpace(entity.FailedCategories)
                ? []
                : entity.FailedCategories.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };
    }
}

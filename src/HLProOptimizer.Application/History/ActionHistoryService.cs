using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.History;

/// <summary>
/// Implementação de <see cref="IActionHistoryService"/>.
/// Nunca propaga falhas de persistência: registrar histórico é um efeito
/// colateral e não pode interromper uma otimização concluída com sucesso.
/// </summary>
public sealed class ActionHistoryService : IActionHistoryService
{
    private readonly IActionHistoryRepository _repository;
    private readonly ILogger<ActionHistoryService> _logger;

    /// <summary>Cria o serviço de histórico.</summary>
    /// <param name="repository">Repositório de ações.</param>
    /// <param name="logger">Logger.</param>
    public ActionHistoryService(IActionHistoryRepository repository, ILogger<ActionHistoryService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        ActionKind kind,
        string title,
        string details = "",
        long bytesAffected = 0,
        long durationMs = 0,
        bool success = true,
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
    {
        var record = new ActionRecord
        {
            Kind = kind,
            Title = title,
            Details = details,
            BytesAffected = bytesAffected,
            DurationMs = durationMs,
            Success = success,
            PayloadJson = payloadJson
        };

        try
        {
            await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Ação registrada: {Kind} - {Title} (sucesso={Success}).", kind, title, success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Não foi possível registrar a ação '{Title}' no histórico.", title);
        }
    }

    /// <inheritdoc />
    public Task RecordOptimizationAsync(OptimizationResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var payload = SafeSerialize(result.Steps.Select(s => new
        {
            s.StepId,
            s.StepName,
            s.Success,
            s.Skipped,
            s.Message,
            s.BytesFreed
        }));

        return RecordAsync(
            ActionKind.Optimization,
            $"Otimização {result.Mode}",
            result.Summary,
            result.TotalBytesFreed,
            (long)result.Duration.TotalMilliseconds,
            result.IsFullySuccessful,
            payload,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordCleanupAsync(CleanupResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        return RecordAsync(
            ActionKind.Cleanup,
            "Limpeza do sistema",
            $"{result.DeletedFileCount} arquivo(s) removido(s), {result.SkippedFileCount} ignorado(s), {result.FailedFileCount} falha(s).",
            result.DeletedBytes,
            (long)result.Duration.TotalMilliseconds,
            !result.HasFailures,
            SafeSerialize(result.Entries),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordAnalysisAsync(ScanReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        return RecordAsync(
            ActionKind.Analysis,
            "Análise do sistema",
            $"{report.Issues.Count} problema(s) encontrado(s) · {report.TotalRecoverableFormatted} recuperáveis.",
            report.TotalRecoverableBytes,
            (long)report.Duration.TotalMilliseconds,
            report.FailedCategories.Count == 0,
            SafeSerialize(new { report.Id, IssueCount = report.Issues.Count, report.FailedCategories }),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActionRecord>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetRecentAsync(count, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao ler o histórico de ações.");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _repository.ClearAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Histórico de ações apagado.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao apagar o histórico de ações.");
        }
    }

    /// <summary>Serialização defensiva do payload (JSON).</summary>
    private string? SafeSerialize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return JsonConvert.SerializeObject(value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível serializar o payload do histórico.");
            return null;
        }
    }
}

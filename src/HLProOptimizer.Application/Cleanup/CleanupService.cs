using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Cleanup;

/// <summary>
/// Fachada de limpeza: compõe todos os <see cref="ICleanupProvider"/> registrados
/// (arquivos do sistema, aplicativos/navegadores e registro), agrupa os alvos por
/// categoria e executa a limpeza roteando cada alvo ao seu provedor de origem.
/// </summary>
public sealed class CleanupService : ICleanupService
{
    /// <summary>Categorias incluídas na "limpeza rápida" (Quick Action do Dashboard).</summary>
    private static readonly IssueCategory[] QuickCategories =
    [
        IssueCategory.TemporaryFiles,
        IssueCategory.SystemCache,
        IssueCategory.LogsAndDumps,
        IssueCategory.RecycleBin
    ];

    private readonly IReadOnlyList<ICleanupProvider> _providers;
    private readonly ILocalizationService _localization;
    private readonly ILogger<CleanupService> _logger;

    /// <summary>Cria o serviço de limpeza.</summary>
    /// <param name="providers">Provedores registrados no DI.</param>
    /// <param name="localization">Localização dos nomes de categoria.</param>
    /// <param name="logger">Logger.</param>
    public CleanupService(
        IEnumerable<ICleanupProvider> providers,
        ILocalizationService localization,
        ILogger<CleanupService> logger)
    {
        _providers = providers.OrderBy(p => p.Order).ThenBy(p => p.Name).ToList();
        _localization = localization;
        _logger = logger;

        logger.LogInformation(
            "CleanupService inicializado com {Count} provedor(es): {Names}.",
            _providers.Count,
            string.Join(", ", _providers.Select(p => p.Name)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CleanupTarget>> GetTargetsAsync(
        OptimizationLevel level,
        CancellationToken cancellationToken = default)
        => await ScanAllAsync(level, progress: null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CleanupCategoryGroup>> ScanAsync(
        OptimizationLevel level,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var targets = await ScanAllAsync(level, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new ScanProgress(
            IssueCategory.TemporaryFiles,
            "Concluído",
            _providers.Count,
            _providers.Count,
            $"Verificação concluída: {targets.Count} alvo(s), {ByteFormat.Format(targets.Sum(t => t.EstimatedBytes))} recuperáveis."));

        return GroupByCategory(targets);
    }

    /// <inheritdoc />
    public async Task<CleanupResult> CleanAsync(
        IReadOnlyList<CleanupTarget> targets,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var started = DateTime.Now;
        var selected = targets.Where(t => t.IsSelected).ToList();

        if (selected.Count == 0)
        {
            _logger.LogInformation("Limpeza solicitada sem nenhum alvo selecionado.");
            return CleanupResult.Empty;
        }

        var entries = new List<CleanupEntryResult>();
        var deletedFiles = 0;
        var deletedBytes = 0L;
        var skippedFiles = 0;
        var failedFiles = 0;

        // Roteia cada alvo para o provedor que o descobriu e executa em lote.
        var groups = selected
            .GroupBy(t => string.IsNullOrWhiteSpace(t.ProviderName) ? ResolveProviderName(t.Category) : t.ProviderName)
            .ToList();

        for (var index = 0; index < groups.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var group = groups[index];
            var groupTargets = group.ToList();
            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Name, group.Key, StringComparison.OrdinalIgnoreCase));

            if (provider is null)
            {
                _logger.LogWarning(
                    "Nenhum provedor encontrado para o grupo '{Group}'; {Count} alvo(s) ignorado(s).",
                    group.Key, groupTargets.Count);

                entries.AddRange(groupTargets.Select(t => new CleanupEntryResult(
                    t.Id, t.Name, 0, 0, t.FileCount, false, "Provedor de limpeza não encontrado.")));

                continue;
            }

            progress?.Report(new ScanProgress(
                provider.Category,
                provider.Name,
                index,
                groups.Count,
                $"Limpando {provider.Name} ({groupTargets.Count} alvo(s))..."));

            try
            {
                var result = await provider.CleanAsync(groupTargets, progress, cancellationToken).ConfigureAwait(false);

                entries.AddRange(result.Entries);
                deletedFiles += result.DeletedFileCount;
                deletedBytes += result.DeletedBytes;
                skippedFiles += result.SkippedFileCount;
                failedFiles += result.FailedFileCount;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Limpeza cancelada pelo usuário durante o provedor {Provider}.", provider.Name);
                throw;
            }
            catch (Exception ex)
            {
                // Um provedor com falha não pode impedir os demais (isolamento de falhas).
                _logger.LogError(ex, "Falha no provedor de limpeza {Provider}.", provider.Name);

                entries.AddRange(groupTargets.Select(t => new CleanupEntryResult(
                    t.Id, t.Name, 0, 0, t.FileCount, false, ex.Message)));

                failedFiles += groupTargets.Count;
            }
        }

        var completed = DateTime.Now;

        _logger.LogInformation(
            "Limpeza concluída em {Duration}: {Files} arquivo(s), {Bytes} bytes liberados, {Skipped} ignorado(s), {Failed} falha(s).",
            completed - started, deletedFiles, deletedBytes, skippedFiles, failedFiles);

        return new CleanupResult(started, completed, deletedFiles, deletedBytes, skippedFiles, failedFiles, entries);
    }

    /// <inheritdoc />
    public async Task<CleanupResult> QuickCleanAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var targets = await ScanAllAsync(OptimizationLevel.Balanced, progress, cancellationToken).ConfigureAwait(false);

        var quickTargets = targets
            .Where(t => QuickCategories.Contains(t.Category) && t.IsSafe)
            .ToList();

        foreach (var target in quickTargets)
        {
            target.IsSelected = true;
        }

        _logger.LogInformation("Limpeza rápida: {Count} alvo(s) selecionado(s).", quickTargets.Count);

        return await CleanAsync(quickTargets, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executa o scan de todos os provedores em paralelo (TPL), reporta progresso
    /// por provedor e aplica a política do nível de otimização.
    /// </summary>
    private async Task<IReadOnlyList<CleanupTarget>> ScanAllAsync(
        OptimizationLevel level,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_providers.Count == 0)
        {
            _logger.LogWarning("Nenhum ICleanupProvider registrado; nada a verificar.");
            return [];
        }

        var total = _providers.Count;
        var completed = 0;

        // Dispara todos os scans em paralelo e acompanha a conclusão na ordem
        // declarada dos provedores, para que a barra de progresso seja previsível.
        var pending = _providers
            .Select(provider => (Provider: provider, Task: ScanProviderAsync(provider, level, cancellationToken)))
            .ToList();

        foreach (var (provider, task) in pending)
        {
            progress?.Report(new ScanProgress(
                provider.Category,
                provider.Name,
                completed,
                total,
                $"Verificando {provider.Name}..."));

            await task.ConfigureAwait(false);
            completed++;

            progress?.Report(new ScanProgress(
                provider.Category,
                provider.Name,
                completed,
                total,
                $"{provider.Name} verificado."));
        }

        var all = new List<CleanupTarget>();

        foreach (var (_, task) in pending)
        {
            all.AddRange(task.Result);
        }

        return ApplyLevelPolicy(all, level);
    }

    /// <summary>Executa o scan de um provedor com tratamento de falha isolado.</summary>
    private async Task<IReadOnlyList<CleanupTarget>> ScanProviderAsync(
        ICleanupProvider provider,
        OptimizationLevel level,
        CancellationToken cancellationToken)
    {
        try
        {
            var targets = await provider.ScanAsync(level, cancellationToken).ConfigureAwait(false);

            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target.ProviderName))
                {
                    target.ProviderName = provider.Name;
                }
            }

            _logger.LogDebug(
                "Provedor {Provider} encontrou {Count} alvo(s) ({Bytes}).",
                provider.Name,
                targets.Count,
                ByteFormat.Format(targets.Sum(t => t.EstimatedBytes)));

            return targets;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao verificar o provedor de limpeza {Provider}.", provider.Name);
            return [];
        }
    }

    /// <summary>
    /// Aplica a política do nível de otimização: no modo Conservador apenas alvos
    /// 100% seguros; no Agressivo todos os alvos são exibidos.
    /// </summary>
    private static IReadOnlyList<CleanupTarget> ApplyLevelPolicy(IReadOnlyList<CleanupTarget> targets, OptimizationLevel level)
    {
        return level switch
        {
            OptimizationLevel.Conservative => targets.Where(t => t.IsSafe).ToList(),
            OptimizationLevel.Balanced => targets.Where(t => t.IsSafe || t.Severity < Severity.High).ToList(),
            _ => targets.ToList()
        };
    }

    /// <summary>Agrupa os alvos por categoria, na ordem de exibição definida.</summary>
    private IReadOnlyList<CleanupCategoryGroup> GroupByCategory(IReadOnlyList<CleanupTarget> targets)
    {
        var groups = new List<CleanupCategoryGroup>();

        foreach (var category in CategoryNames.CleanupOrder)
        {
            var categoryTargets = targets.Where(t => t.Category == category).ToList();

            if (categoryTargets.Count == 0)
            {
                continue;
            }

            groups.Add(new CleanupCategoryGroup(
                category,
                _localization[CategoryNames.LocalizationKey(category)],
                categoryTargets.OrderByDescending(t => t.EstimatedBytes).ToList()));
        }

        // Categorias fora da ordem padrão (ex.: vindas de plugins) entram ao final.
        foreach (var extra in targets
                     .Where(t => !CategoryNames.CleanupOrder.Contains(t.Category))
                     .GroupBy(t => t.Category))
        {
            groups.Add(new CleanupCategoryGroup(
                extra.Key,
                _localization[CategoryNames.LocalizationKey(extra.Key)],
                extra.OrderByDescending(t => t.EstimatedBytes).ToList()));
        }

        return groups;
    }

    /// <summary>Resolve o nome do provedor padrão para uma categoria (fallback).</summary>
    private string ResolveProviderName(IssueCategory category)
    {
        return _providers.FirstOrDefault(p => p.Category == category)?.Name
            ?? _providers.FirstOrDefault()?.Name
            ?? string.Empty;
    }
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Cleanup;

/// <summary>
/// Máquina comum dos provedores de limpeza baseados em arquivos (sistema,
/// navegadores, logs). Cada provedor derivado só declara o seu catálogo
/// (<see cref="BuildDefinitions"/>) e a sua identidade — o scan, a medição e a
/// remoção tolerante a falhas vivem aqui (DRY).
/// </summary>
/// <remarks>
/// <para>
/// <b>Catálogo declarativo.</b> Um alvo é descrito por caminhos (resolvidos em
/// tempo de execução), padrões de nome, idade mínima e nível mínimo de
/// agressividade. Adicionar um alvo é acrescentar uma entrada ao catálogo, sem
/// tocar na lógica de limpeza (Open/Closed).
/// </para>
/// <para>
/// <b>Idade mínima</b> protege arquivos em uso: temporários com menos de uma hora
/// provavelmente pertencem a um processo vivo.
/// </para>
/// <para>
/// <b>Segurança</b>: a limpeza só apaga arquivos retornados pela enumeração com os
/// padrões do próprio alvo. Um alvo cujo Id não existe no catálogo é recusado —
/// nunca apagamos "no escuro".
/// </para>
/// </remarks>
public abstract class FileCleanupProviderBase : ICleanupProvider
{
    /// <summary>Idade mínima para temporários (arquivos mais novos podem estar em uso).</summary>
    protected static readonly TimeSpan TempMinimumAge = TimeSpan.FromHours(1);

    private readonly IFileSystemService _fileSystem;
    private readonly ILogger _logger;
    private IReadOnlyList<FileCleanupDefinition>? _definitions;

    /// <summary>Cria o provedor.</summary>
    /// <param name="fileSystem">Serviço de arquivos tolerante a falhas.</param>
    /// <param name="logger">Logger.</param>
    protected FileCleanupProviderBase(IFileSystemService fileSystem, ILogger logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract IssueCategory Category { get; }

    /// <inheritdoc />
    public abstract int Order { get; }

    /// <inheritdoc />
    public virtual bool RequiresAdmin => false;

    /// <summary>Catálogo de alvos do provedor.</summary>
    protected abstract IReadOnlyList<FileCleanupDefinition> BuildDefinitions();

    /// <summary>Catálogo resolvido uma única vez por instância (provedores são singletons).</summary>
    protected IReadOnlyList<FileCleanupDefinition> Definitions => _definitions ??= BuildDefinitions();

    /// <inheritdoc />
    public async Task<IReadOnlyList<CleanupTarget>> ScanAsync(OptimizationLevel level, CancellationToken cancellationToken = default)
    {
        var targets = new List<CleanupTarget>();

        foreach (var definition in Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Nível de agressividade: alvos arriscados só entram quando o usuário pediu.
            if (definition.MinimumLevel > level)
            {
                continue;
            }

            try
            {
                var target = await ScanTargetAsync(definition, cancellationToken).ConfigureAwait(false);

                if (target is not null)
                {
                    targets.Add(target);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao medir o alvo de limpeza {Target}.", definition.Id);
            }
        }

        _logger.LogInformation(
            "{Provider}: {Count} alvo(s), {Bytes} recuperáveis.",
            Name,
            targets.Count,
            ByteFormat.Format(targets.Sum(t => t.EstimatedBytes)));

        return targets.OrderByDescending(t => t.EstimatedBytes).ToList();
    }

    /// <inheritdoc />
    public async Task<CleanupResult> CleanAsync(
        IReadOnlyList<CleanupTarget> targets,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var startedAt = DateTime.Now;
        var entries = new List<CleanupEntryResult>();
        var deletedFiles = 0;
        long deletedBytes = 0;
        var skippedFiles = 0;
        var failedFiles = 0;
        var processed = 0;

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            processed++;

            progress?.Report(new ScanProgress(
                target.Category,
                target.Name,
                processed - 1,
                targets.Count,
                $"Limpando {target.Name}..."));

            var definition = Definitions.FirstOrDefault(d => d.Id == target.Id);

            if (definition is null)
            {
                // Alvo desconhecido pelo catálogo: recusamos apagar qualquer coisa.
                entries.Add(new CleanupEntryResult(target.Id, target.Name, 0, 0, 0, false, "Alvo não reconhecido pelo catálogo de limpeza."));
                failedFiles++;

                continue;
            }

            try
            {
                var result = definition.IsRecycleBin
                    ? await EmptyRecycleBinAsync(cancellationToken).ConfigureAwait(false)
                    : await CleanTargetAsync(target, definition, cancellationToken).ConfigureAwait(false);

                entries.Add(new CleanupEntryResult(
                    target.Id,
                    target.Name,
                    result.DeletedCount,
                    result.DeletedBytes,
                    result.SkippedCount,
                    result.FailedCount == 0,
                    result.FailedCount > 0 ? string.Join("; ", result.Errors.Take(3)) : null));

                deletedFiles += result.DeletedCount;
                deletedBytes += result.DeletedBytes;
                skippedFiles += result.SkippedCount;
                failedFiles += result.FailedCount;

                _logger.LogInformation(
                    "{Target}: {Files} arquivo(s) removido(s), {Bytes} liberado(s), {Skipped} ignorado(s).",
                    target.Name,
                    result.DeletedCount,
                    ByteFormat.Format(result.DeletedBytes),
                    result.SkippedCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                entries.Add(new CleanupEntryResult(target.Id, target.Name, 0, 0, 0, false, "Requer privilégios de administrador."));
                _logger.LogWarning(ex, "Sem permissão para limpar {Target}.", target.Name);
            }
            catch (Exception ex)
            {
                entries.Add(new CleanupEntryResult(target.Id, target.Name, 0, 0, 0, false, ex.Message));
                _logger.LogError(ex, "Falha inesperada ao limpar {Target}.", target.Name);
            }
        }

        progress?.Report(new ScanProgress(
            Category,
            "Concluído",
            targets.Count,
            targets.Count,
            $"Limpeza concluída: {ByteFormat.Format(deletedBytes)} liberados."));

        return new CleanupResult(startedAt, DateTime.Now, deletedFiles, deletedBytes, skippedFiles, failedFiles, entries);
    }

    // ---------------------------------------------------------------------
    // Scan / medição
    // ---------------------------------------------------------------------

    /// <summary>Mede um alvo e devolve o <see cref="CleanupTarget"/> (ou <c>null</c> quando vazio).</summary>
    private async Task<CleanupTarget?> ScanTargetAsync(FileCleanupDefinition definition, CancellationToken cancellationToken)
    {
        // A Lixeira usa a API de shell, não um diretório.
        if (definition.IsRecycleBin)
        {
            var recycleBinBytes = await _fileSystem.GetRecycleBinSizeAsync(cancellationToken).ConfigureAwait(false);

            return recycleBinBytes > 0
                ? new CleanupTarget
                {
                    Id = definition.Id,
                    Name = definition.Name,
                    Description = definition.Description,
                    Category = definition.Category,
                    Paths = ["shell:RecycleBinFolder"],
                    EstimatedBytes = recycleBinBytes,
                    FileCount = 0,
                    IsSafe = definition.IsSafe,
                    RequiresAdmin = false,
                    Severity = definition.Severity,
                    IsSelected = definition.SelectedByDefault
                }
                : null;
        }

        var paths = definition.ResolvePaths().Where(_fileSystem.DirectoryExists).ToList();

        if (paths.Count == 0)
        {
            return null;
        }

        var (bytes, files) = await MeasureAsync(paths, definition, cancellationToken).ConfigureAwait(false);

        if (bytes == 0 && files == 0)
        {
            return null;
        }

        return new CleanupTarget
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Category = definition.Category,
            Paths = paths,
            EstimatedBytes = bytes,
            FileCount = files,
            IsSafe = definition.IsSafe,
            RequiresAdmin = definition.RequiresAdmin,
            Severity = definition.Severity,
            IsSelected = definition.SelectedByDefault && definition.IsSafe
        };
    }

    /// <summary>Mede tamanho e quantidade de arquivos dos caminhos de um alvo.</summary>
    private async Task<(long Bytes, int Files)> MeasureAsync(
        IReadOnlyList<string> paths,
        FileCleanupDefinition definition,
        CancellationToken cancellationToken)
    {
        long bytes = 0;
        var files = 0;

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var file in await _fileSystem.EnumerateFilesAsync(path, definition.SearchPatterns, definition.Recursive, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var info = new FileInfo(file);

                    if (!info.Exists)
                    {
                        continue;
                    }

                    if (definition.MinimumAge is { } age && DateTime.Now - info.LastWriteTime < age)
                    {
                        continue;
                    }

                    bytes += info.Length;
                    files++;
                }
                catch (Exception)
                {
                    // Arquivo inacessível (permissões/reparse point): ignorado na medição.
                }
            }
        }

        return (bytes, files);
    }

    // ---------------------------------------------------------------------
    // Limpeza
    // ---------------------------------------------------------------------

    /// <summary>
    /// Limpa um alvo respeitando padrões, recursividade e idade mínima do catálogo.
    /// </summary>
    private async Task<DeleteFilesResult> CleanTargetAsync(
        CleanupTarget target,
        FileCleanupDefinition definition,
        CancellationToken cancellationToken)
    {
        var candidates = new List<string>();

        foreach (var path in target.Paths)
        {
            if (!_fileSystem.DirectoryExists(path))
            {
                continue;
            }

            candidates.AddRange(await _fileSystem
                .EnumerateFilesAsync(path, definition.SearchPatterns, definition.Recursive, cancellationToken)
                .ConfigureAwait(false));
        }

        if (definition.MinimumAge is { } age)
        {
            candidates = candidates.Where(file =>
            {
                try
                {
                    return DateTime.Now - File.GetLastWriteTime(file) >= age;
                }
                catch (Exception)
                {
                    // Sem data (acesso negado): não arrisca apagar.
                    return false;
                }
            }).ToList();
        }

        if (candidates.Count == 0)
        {
            return DeleteFilesResult.Empty;
        }

        var result = await _fileSystem.DeleteFilesAsync(candidates, cancellationToken).ConfigureAwait(false);

        // Só remove pastas vazias quando a limpeza foi recursiva e sem filtros de nome —
        // com filtros, a pasta raiz ainda guarda conteúdo que não deve ser tocado.
        if (definition.Recursive && definition.SearchPatterns is null)
        {
            foreach (var path in target.Paths)
            {
                try
                {
                    await _fileSystem.CleanDirectoryAsync(path, definition.MinimumAge, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Não foi possível remover as pastas vazias de {Path}.", path);
                }
            }
        }

        return result;
    }

    /// <summary>Esvazia a Lixeira e reporta como resultado de exclusão.</summary>
    private async Task<DeleteFilesResult> EmptyRecycleBinAsync(CancellationToken cancellationToken)
    {
        var bytes = await _fileSystem.EmptyRecycleBinAsync(cancellationToken).ConfigureAwait(false);

        return bytes > 0
            ? new DeleteFilesResult(1, bytes, 0, 0, [])
            : new DeleteFilesResult(0, 0, 0, 0, ["A Lixeira já estava vazia ou não pôde ser esvaziada."]);
    }

    /// <summary>
    /// Expande perfis de aplicativos (ex.: <c>User Data\Default</c>, <c>Profile 1</c>).
    /// </summary>
    /// <param name="basePath">Pasta que contém os perfis.</param>
    /// <param name="relativePath">Caminho relativo dentro de cada perfil.</param>
    /// <returns>Caminhos existentes.</returns>
    protected static IReadOnlyList<string> ExpandProfiles(string basePath, string relativePath)
    {
        try
        {
            if (!Directory.Exists(basePath))
            {
                return [];
            }

            return Directory
                .EnumerateDirectories(basePath)
                .Select(profile => Path.Combine(profile, relativePath))
                .Where(Directory.Exists)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Descrição declarativa de um alvo de limpeza de arquivos.</summary>
    /// <param name="Id">Identificador estável.</param>
    /// <param name="Name">Nome exibido.</param>
    /// <param name="Description">Descrição.</param>
    /// <param name="Category">Categoria de problema.</param>
    /// <param name="PathResolver">Função que resolve os caminhos em tempo de execução.</param>
    /// <param name="SearchPatterns">Padrões de nome de arquivo (null = todos).</param>
    /// <param name="Recursive">Se percorre subpastas.</param>
    /// <param name="Severity">Severidade exibida.</param>
    /// <param name="RequiresAdmin">Se exige administrador.</param>
    /// <param name="IsSafe">Se a remoção não tem efeito colateral relevante.</param>
    /// <param name="MinimumLevel">Nível de agressividade mínimo para incluir o alvo.</param>
    /// <param name="MinimumAge">Idade mínima dos arquivos (protege os que estão em uso).</param>
    /// <param name="SelectedByDefault">Se vem marcado no scan.</param>
    /// <param name="IsRecycleBin">Se o alvo é a Lixeira (usa API de shell).</param>
    protected sealed record FileCleanupDefinition(
        string Id,
        string Name,
        string Description,
        IssueCategory Category,
        Func<string[]> PathResolver,
        string[]? SearchPatterns = null,
        bool Recursive = true,
        Severity Severity = Severity.Low,
        bool RequiresAdmin = false,
        bool IsSafe = true,
        OptimizationLevel MinimumLevel = OptimizationLevel.Conservative,
        TimeSpan? MinimumAge = null,
        bool SelectedByDefault = true,
        bool IsRecycleBin = false)
    {
        /// <summary>Resolve os caminhos existentes (ignora entradas vazias e duplicadas).</summary>
        public IReadOnlyList<string> ResolvePaths() =>
            PathResolver()
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}

using System.Runtime.InteropServices;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Platform;

/// <summary>
/// Operações de arquivo tolerantes a falhas: arquivos em uso ou protegidos são
/// contabilizados como "ignorados", nunca como erro fatal.
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    private const int MaxErrorsCollected = 25;

    private readonly ILogger<FileSystemService> _logger;

    /// <summary>Cria o serviço de arquivos.</summary>
    /// <param name="logger">Logger.</param>
    public FileSystemService(ILogger<FileSystemService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool DirectoryExists(string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    /// <inheritdoc />
    public bool FileExists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    /// <inheritdoc />
    public async Task<long> GetDirectorySizeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!DirectoryExists(path))
        {
            return 0;
        }

        long total = 0;

        await foreach (var file in EnumerateAsync(path, ["*"], true, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Arquivo bloqueado/removido durante a medição: ignora.
            }
        }

        return total;
    }

    /// <inheritdoc />
    public async Task<int> CountFilesAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!DirectoryExists(path))
        {
            return 0;
        }

        var count = 0;

        await foreach (var _ in EnumerateAsync(path, ["*"], true, cancellationToken).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> EnumerateFilesAsync(
        string path,
        IEnumerable<string>? searchPatterns = null,
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        if (!DirectoryExists(path))
        {
            return [];
        }

        var patterns = searchPatterns?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList() ?? ["*"];

        if (patterns.Count == 0)
        {
            patterns = ["*"];
        }

        var files = new List<string>();

        await foreach (var file in EnumerateAsync(path, patterns, recursive, cancellationToken).ConfigureAwait(false))
        {
            files.Add(file);
        }

        return files;
    }

    /// <inheritdoc />
    public async Task<DeleteFilesResult> DeleteFilesAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var deleted = 0;
        var deletedBytes = 0L;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var file in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(file))
            {
                continue;
            }

            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                var info = new FileInfo(file);

                // Nunca apagar links/atalhos que apontam para fora da pasta alvo.
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    skipped++;
                    continue;
                }

                if (info.IsReadOnly)
                {
                    info.IsReadOnly = false;
                }

                var size = info.Length;
                info.Delete();

                deleted++;
                deletedBytes += size;
            }
            catch (IOException)
            {
                // Arquivo em uso: esperado em pastas de temp.
                skipped++;
            }
            catch (UnauthorizedAccessException)
            {
                skipped++;
            }
            catch (Exception ex)
            {
                failed++;

                if (errors.Count < MaxErrorsCollected)
                {
                    errors.Add($"{file}: {ex.Message}");
                }

                _logger.LogDebug(ex, "Falha inesperada ao excluir {File}.", file);
            }
        }

        return new DeleteFilesResult(deleted, deletedBytes, skipped, failed, errors);
    }

    /// <inheritdoc />
    public async Task<DeleteFilesResult> CleanDirectoryAsync(
        string directoryPath,
        TimeSpan? minimumAge = null,
        CancellationToken cancellationToken = default)
    {
        if (!DirectoryExists(directoryPath))
        {
            return DeleteFilesResult.Empty;
        }

        var cutoff = minimumAge is { } age ? DateTime.Now - age : DateTime.MaxValue;

        var files = (await EnumerateFilesAsync(directoryPath, null, true, cancellationToken).ConfigureAwait(false))
            .Where(f => IsOlderThan(f, cutoff))
            .ToList();

        var result = await DeleteFilesAsync(files, cancellationToken).ConfigureAwait(false);

        // Remove subdiretórios que ficaram vazios (preserva a raiz informada).
        await RemoveEmptyDirectoriesAsync(directoryPath, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    public Task<long> GetRecycleBinSizeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(QueryRecycleBin().Size);
    }

    /// <inheritdoc />
    public Task<long> EmptyRecycleBinAsync(CancellationToken cancellationToken = default)
    {
        var before = QueryRecycleBin();

        if (before.Size <= 0)
        {
            return Task.FromResult(0L);
        }

        try
        {
            var code = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, NativeMethods.RecycleBinNoUi);

            // S_OK (0), S_FALSE (1) e "já vazia" são resultados aceitáveis.
            if (code is not (0 or 1) && code != 0x8000FFFF)
            {
                _logger.LogWarning("SHEmptyRecycleBin retornou 0x{Code:X8}.", code);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao esvaziar a Lixeira.");
            return Task.FromResult(0L);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(before.Size);
    }

    /// <inheritdoc />
    public bool TryDeleteFile(string filePath, out string? error)
    {
        error = null;

        try
        {
            if (!File.Exists(filePath))
            {
                return true;
            }

            var info = new FileInfo(filePath);

            if (info.IsReadOnly)
            {
                info.IsReadOnly = false;
            }

            info.Delete();

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Enumeração assíncrona e resiliente de arquivos.</summary>
    private async IAsyncEnumerable<string> EnumerateAsync(
        string path,
        IReadOnlyList<string> patterns,
        bool recursive,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var queue = new Queue<string>();
        queue.Enqueue(path);

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = queue.Dequeue();

            string[] files;
            string[] directories;

            try
            {
                files = patterns.Count == 1
                    ? Directory.GetFiles(current, patterns[0], SearchOption.TopDirectoryOnly)
                    : patterns.SelectMany(p => SafeGetFiles(current, p)).Distinct().ToArray();

                directories = recursive
                    ? Directory.GetDirectories(current)
                    : [];
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                _logger.LogDebug(ex, "Pasta inacessível durante a enumeração: {Path}.", current);
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var directory in directories)
            {
                // Nunca seguir reparse points (junctions/symlinks) - evita loops e
                // a exclusão acidental de conteúdo fora da pasta alvo.
                try
                {
                    var attributes = File.GetAttributes(directory);

                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                queue.Enqueue(directory);
            }

            await Task.Yield();
        }
    }

    /// <summary>Remove diretórios vazios (de baixo para cima).</summary>
    private async Task RemoveEmptyDirectoriesAsync(string rootPath, CancellationToken cancellationToken)
    {
        try
        {
            var directories = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length)
                .ToList();

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    if (Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        continue;
                    }

                    Directory.Delete(directory, recursive: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Pasta em uso: mantém.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao limpar diretórios vazios em {Path}.", rootPath);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Consulta tamanho e quantidade de itens da Lixeira.</summary>
    private static (long Size, long Items) QueryRecycleBin()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (0, 0);
        }

        try
        {
            var info = new NativeMethods.SHQUERYRBINFO();
            info.cbSize = Marshal.SizeOf(info);

            var result = NativeMethods.SHQueryRecycleBin(null, ref info);

            return result == 0 ? (info.iSize, info.iNumItems) : (0, 0);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    /// <summary>Enumeração de arquivos com padrão único, ignorando falhas.</summary>
    private IEnumerable<string> SafeGetFiles(string path, string pattern)
    {
        try
        {
            return Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogDebug(ex, "Falha ao listar {Pattern} em {Path}.", pattern, path);
            return [];
        }
    }

    /// <summary>Verifica se o arquivo é mais antigo que o corte informado.</summary>
    private static bool IsOlderThan(string file, DateTime cutoff)
    {
        if (cutoff == DateTime.MaxValue)
        {
            return true;
        }

        try
        {
            return File.GetLastWriteTime(file) < cutoff;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

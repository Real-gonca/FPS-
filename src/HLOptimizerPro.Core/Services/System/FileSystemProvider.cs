using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services.System;

/// <summary>
/// Acesso real ao sistema de ficheiros com medição de espaço libertado.
/// A limpeza ignora ficheiros em uso (comportamento correto: o Windows mantém SOs e
/// aplicações abertas) e nunca segue links simbólicos/junções para não sair da pasta alvo.
/// </summary>
public sealed class FileSystemProvider : IFileSystemProvider
{
    private readonly ILogger<FileSystemProvider> _logger;

    public FileSystemProvider(ILogger<FileSystemProvider> logger) => _logger = logger;

    public bool DirectoryExists(string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    public bool FileExists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    public Models.CleanupSummary ClearDirectory(string path, bool recursive = true, Predicate<string>? filter = null)
    {
        var summary = new Models.CleanupSummary();

        if (!Directory.Exists(path))
        {
            summary.Errors = new[] { $"Pasta inexistente: {path}" };
            return summary;
        }

        var files = EnumerateSafe(path, recursive);

        foreach (var file in files)
        {
            try
            {
                if (filter is not null && !filter(file)) continue;

                var info = new FileInfo(file);
                var size = info.Exists ? info.Length : 0;

                // Limpa o atributo só-leitura/oculto que impede a remoção de caches antigos.
                info.Attributes = FileAttributes.Normal;
                File.Delete(file);

                summary.FilesDeleted++;
                summary.BytesFreed += size;
            }
            catch (UnauthorizedAccessException)
            {
                // Ficheiro em uso ou protegido — normal em Temp; ignorar em silêncio.
            }
            catch (IOException)
            {
                // Ficheiro bloqueado por outro processo — normal em Temp.
            }
            catch (Exception ex)
            {
                summary.Errors = summary.Errors.Append($"{Path.GetFileName(file)}: {ex.Message}").ToList();
            }
        }

        // Segundo passo: remover subpastas já vazias (mantém a pasta raiz intacta).
        if (recursive)
        {
            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch
                {
                    // Pasta em uso — ignorar.
                }
            }
        }

        summary.Details = summary.Details.Append($"{path}: {summary.FilesDeleted} ficheiros, {Helpers.Formatters.Bytes(summary.BytesFreed)}").ToList();
        _logger.LogInformation("Limpeza de {Path}: {Files} ficheiros ({Bytes})", path, summary.FilesDeleted, Helpers.Formatters.Bytes(summary.BytesFreed));
        return summary;
    }

    public long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;

        long total = 0;
        foreach (var file in EnumerateSafe(path, recursive: true))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // Ignorar ficheiros inacessíveis.
            }
        }

        return total;
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public void EnsureDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path)) Directory.CreateDirectory(path);
    }

    public string GetTempPath() => Path.GetTempPath();

    public IReadOnlyList<string> EnumerateFiles(string path, bool recursive = true) => EnumerateSafe(path, recursive);

    public void CopyFile(string source, string destination, bool overwrite = true)
    {
        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(source, destination, overwrite);
    }

    private IReadOnlyList<string> EnumerateSafe(string path, bool recursive)
    {
        var results = new List<string>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,   // nunca tocar em ficheiros de sistema
            ReturnSpecialDirectories = false,
        };

        try
        {
            // EnumerateFiles com IgnoreInaccessible não segue junções para fora da árvore.
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                if (IsSymbolicLinkTarget(file)) continue;
                results.Add(file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Enumeração falhou em {Path}", path);
        }

        return results;
    }

    private static bool IsSymbolicLinkTarget(string file)
    {
        try
        {
            return (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;   // em caso de dúvida, não mexer
        }
    }
}

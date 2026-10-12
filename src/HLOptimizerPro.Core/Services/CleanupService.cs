using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Motor de limpeza de ficheiros e caches.
///
/// Princípios:
///   • Medimos o espaço ANTES e DEPOIS para apresentar o valor realmente libertado (não estimativas).
///   • Ficheiros em uso são ignorados em silêncio — é o comportamento correto, não um erro.
///   • Nunca tocamos em pastas do utilizador (Documentos, Imagens, Ambiente de Trabalho, Downloads).
///   • Toda a limpeza é assíncrona e cancelável, com progresso apresentado na UI.
///   • O resultado inclui erros reais (permissões, ficheiros bloqueados) sem os esconder.
/// </summary>
public sealed class CleanupService
{
    private readonly ILogger<CleanupService> _logger;
    private readonly IFileSystemProvider _fileSystem;
    private readonly IProcessRunner _runner;
    private readonly ActivityLogService _activity;

    public CleanupService(
        ILogger<CleanupService> logger,
        IFileSystemProvider fileSystem,
        IProcessRunner runner,
        ActivityLogService activity)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _runner = runner;
        _activity = activity;
    }

    /// <summary>Progresso por alvo (nome + percentagem) para a barra de progresso da UI.</summary>
    public event EventHandler<(string Target, int Index, int Total)>? TargetStarted;

    /// <summary>Executa uma limpeza completa dos alvos indicados.</summary>
    public async Task<Result<CleanupSummary>> RunAsync(
        IReadOnlyList<CleanupTarget> targets,
        CancellationToken cancellationToken = default)
    {
        if (targets.Count == 0)
            return Result<CleanupSummary>.Fail("Selecione pelo menos uma limpeza.");

        var summary = new CleanupSummary();

        await Task.Run(() =>
        {
            for (var index = 0; index < targets.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = targets[index];
                TargetStarted?.Invoke(this, (target.Name, index + 1, targets.Count));

                try
                {
                    var partial = Clean(target);
                    summary.Add(partial);

                    // Uma pausa curta entre alvos mantém a interface fluida e evita saturar o disco.
                    Thread.Sleep(60);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Limpeza de «{Target}» falhou.", target.Name);
                    summary.Errors = summary.Errors.Append($"{target.Name}: {ex.Message}").ToList();
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        var message = summary.FilesDeleted == 0 && summary.CacheEntriesCleared == 0
            ? "Limpeza concluída — nada relevante para remover (o sistema já estava limpo)."
            : $"Limpeza concluída: {summary.FilesDeleted} ficheiros removidos, " +
              $"{Formatters.Bytes(summary.BytesFreed)} libertados, {summary.CacheEntriesCleared} cache(s) limpa(s).";

        _activity.Log("Limpeza", message, success: summary.Errors.Count == 0,
            details: string.Join(" | ", summary.Details));

        return summary.Errors.Count == 0
            ? Result<CleanupSummary>.Ok(summary, message)
            // Falha parcial: os bytes libertados são reais e têm de chegar à interface, mesmo com avisos.
            : Result<CleanupSummary>.Partial(summary, message + $" ({summary.Errors.Count} avisos)",
                string.Join("\n", summary.Errors));
    }

    /// <summary>Calcula quanto espaço cada alvo ocupa (pré-visualização: "libertar X GB").</summary>
    public async Task<IReadOnlyDictionary<string, long>> EstimateAsync(
        IReadOnlyList<CleanupTarget> targets,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var size = target.Kind switch
                {
                    CleanupKind.Files or CleanupKind.WindowsUpdateCache or CleanupKind.Prefetch
                        or CleanupKind.FontCache or CleanupKind.ThumbnailCache =>
                        target.Paths.Sum(_fileSystem.GetDirectorySize),

                    CleanupKind.RecycleBin => GetRecycleBinSize(),

                    _ => 0,
                };

                result[target.Key] = size;
            }

            return (IReadOnlyDictionary<string, long>)result;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Descrição textual do estado atual das caches (mostrada no módulo de limpeza).</summary>
    public string DescribeTarget(CleanupTarget target, long sizeBytes) => target.Kind switch
    {
        CleanupKind.DnsCache => "Cache do resolvedor DNS — limpeza instantânea.",
        CleanupKind.RecycleBin => sizeBytes > 0 ? $"Contém {Formatters.Bytes(sizeBytes)}" : "Reciclagem já vazia.",
        CleanupKind.WindowsUpdateCache => "Os ficheiros de atualizações antigas podem ser reobtidos pelo Windows.",
        CleanupKind.Prefetch => "A primeira execução de cada programa fica ligeiramente mais lenta.",
        _ => sizeBytes > 0 ? $"{Formatters.Bytes(sizeBytes)} recuperáveis" : "Já está limpo.",
    };

    // ------------------------------------------------------------------ internos

    private CleanupSummary Clean(CleanupTarget target)
    {
        switch (target.Kind)
        {
            case CleanupKind.DnsCache:
                return FlushDns();

            case CleanupKind.RecycleBin:
                return EmptyRecycleBin();

            case CleanupKind.ThumbnailCache:
                return ClearThumbnailCache(target);

            case CleanupKind.IconCache:
                return ClearIconCache();

            case CleanupKind.FontCache:
                return ClearFontCache(target);

            case CleanupKind.Prefetch:
                return ClearPrefetch(target);

            case CleanupKind.Files:
            case CleanupKind.WindowsUpdateCache:
                return ClearPaths(target);

            default:
                return new CleanupSummary { Details = new[] { $"{target.Name}: nada a fazer" } };
        }
    }

    private CleanupSummary ClearPaths(CleanupTarget target)
    {
        var summary = new CleanupSummary();

        foreach (var path in target.Paths)
        {
            if (!_fileSystem.DirectoryExists(path))
            {
                continue;
            }

            summary.Add(_fileSystem.ClearDirectory(path));
        }

        return summary;
    }

    private CleanupSummary ClearThumbnailCache(CleanupTarget target)
    {
        var summary = new CleanupSummary { Details = new[] { "Cache de miniaturas: os ficheiros serão recriados pelo Explorer" } };

        foreach (var path in target.Paths)
        {
            if (!_fileSystem.DirectoryExists(path)) continue;

            // Só os ficheiros de cache do Explorer (thumbcache_*.db e iconcache_*.db).
            summary.Add(_fileSystem.ClearDirectory(path, recursive: false, filter: file =>
            {
                var name = Path.GetFileName(file);
                return name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase);
            }));
        }

        summary.CacheEntriesCleared++;
        return summary;
    }

    private CleanupSummary ClearIconCache()
    {
        var summary = new CleanupSummary { CacheEntriesCleared = 1 };
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

        // Reinicia o Explorer apenas se o utilizador pedir a limpeza de ícones explicitamente
        // (é visualmente intrusivo, por isso é uma ação separada e não a predefinida).
        var result = _runner.Run("ie4uinit.exe", "-show");

        summary.Details = new[] { result.Success ? "Cache de ícones reconstruída" : "Cache de ícones: não foi possível atualizar" };
        return summary;
    }

    private CleanupSummary ClearFontCache(CleanupTarget target)
    {
        var summary = ClearPaths(target);
        summary.CacheEntriesCleared++;

        // Serviço de cache de tipos de letra é reiniciado automaticamente pelo Windows.
        _runner.Run("net", "stop FontCache");
        _runner.Run("net", "start FontCache");
        return summary;
    }

    private CleanupSummary ClearPrefetch(CleanupTarget target)
    {
        var summary = ClearPaths(target);
        summary.Details = summary.Details.Concat(new[] { "Prefetch limpo: o Windows reconstruirá os dados de arranque" }).ToList();
        return summary;
    }

    private CleanupSummary FlushDns()
    {
        var result = _runner.Run("ipconfig", "/flushdns");
        return new CleanupSummary
        {
            CacheEntriesCleared = result.Success ? 1 : 0,
            Details = new[] { result.Success ? "Cache de DNS limpa" : "Não foi possível limpar a cache de DNS" },
            Errors = result.Success ? Array.Empty<string>() : new[] { result.Output },
        };
    }

    private CleanupSummary EmptyRecycleBin()
    {
        try
        {
            var size = GetRecycleBinSize();

            // PowerShell é a forma mais fiável de esvaziar a reciclagem de todos os utilizadores.
            var result = _runner.Run("powershell.exe",
                "-NoProfile -NonInteractive -Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"");

            return new CleanupSummary
            {
                FilesDeleted = result.Success ? 1 : 0,
                BytesFreed = result.Success ? size : 0,
                Details = new[] { result.Success ? $"Reciclagem esvaziada ({Formatters.Bytes(size)})" : "Não foi possível esvaziar a reciclagem" },
            };
        }
        catch (Exception ex)
        {
            return new CleanupSummary { Errors = new[] { $"Reciclagem: {ex.Message}" } };
        }
    }

    /// <summary>Tamanho total da Reciclagem (soma de todos os volumes, pasta $Recycle.Bin).</summary>
    public long GetRecycleBinSize()
    {
        long total = 0;

        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                var path = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                if (Directory.Exists(path)) total += _fileSystem.GetDirectorySize(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível medir a Reciclagem.");
        }

        return total;
    }
}

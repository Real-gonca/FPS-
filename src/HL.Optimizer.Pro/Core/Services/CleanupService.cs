using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.IO;
using System.Diagnostics;

namespace HL.Optimizer.Pro.Core.Services;

public class CleanupService : ICleanupService
{
    private readonly ILogService _log;

    public CleanupService(ILogService log)
    {
        _log = log;
    }

    public async Task<List<CleanupItem>> AnalyzeAsync(IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            var items = new List<CleanupItem>();

            progress?.Report("Analisando arquivos temporários do Windows...");
            items.AddRange(AnalyzePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), "Arquivos Temporários do Windows", "Arquivos temporários do sistema operacional", CleanupCategory.TempWindows));

            progress?.Report("Analisando arquivos temporários do usuário...");
            items.AddRange(AnalyzePath(Path.GetTempPath(), "Arquivos Temporários do Usuário", "Arquivos temporários do usuário atual", CleanupCategory.TempUser));

            progress?.Report("Analisando cache...");
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            items.AddRange(AnalyzePath(Path.Combine(localAppData, "Temp"), "Cache Local", "Cache de aplicativos", CleanupCategory.Cache));

            progress?.Report("Analisando logs...");
            items.AddRange(AnalyzePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs"), "Logs do Windows", "Arquivos de log antigos", CleanupCategory.Logs));

            progress?.Report("Analisando Windows Update Cache...");
            items.AddRange(AnalyzePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"), "Cache do Windows Update", "Arquivos de atualização já instalados", CleanupCategory.WindowsUpdate));

            progress?.Report("Analisando miniaturas...");
            items.AddRange(AnalyzePath(Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"), "Cache de Miniaturas", "Miniaturas de imagens", CleanupCategory.Thumbnails, "thumbcache_*.db"));

            progress?.Report("Analisando lixeira...");
            var recycleSize = GetRecycleBinSizeSync();
            if (recycleSize > 0)
            {
                items.Add(new CleanupItem
                {
                    Name = "Lixeira",
                    Description = "Arquivos na lixeira",
                    Path = "$Recycle.Bin",
                    SizeBytes = recycleSize,
                    FileCount = 0,
                    Category = CleanupCategory.RecycleBin
                });
            }

            progress?.Report("Analisando cache de navegadores...");
            items.AddRange(AnalyzeBrowserCaches());

            return items.Where(i => i.SizeBytes > 0).ToList();
        });
    }

    private List<CleanupItem> AnalyzePath(string path, string name, string description, CleanupCategory category, string searchPattern = "*")
    {
        var result = new List<CleanupItem>();
        try
        {
            if (!Directory.Exists(path)) return result;

            var dirInfo = new DirectoryInfo(path);
            long totalSize = 0;
            int fileCount = 0;

            try
            {
                var files = dirInfo.GetFiles(searchPattern, SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        // Skip files in use or very recent (< 1 day) for safety
                        if (file.LastWriteTime > DateTime.Now.AddDays(-1) && category != CleanupCategory.Thumbnails) continue;
                        totalSize += file.Length;
                        fileCount++;
                    }
                    catch { }
                }
            }
            catch { }

            if (totalSize > 0)
            {
                result.Add(new CleanupItem
                {
                    Name = name,
                    Description = description,
                    Path = path,
                    SizeBytes = totalSize,
                    FileCount = fileCount,
                    Category = category
                });
            }
        }
        catch { }
        return result;
    }

    private List<CleanupItem> AnalyzeBrowserCaches()
    {
        var list = new List<CleanupItem>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var browserPaths = new[]
        {
            (Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), "Chrome Cache", "Cache do Google Chrome"),
            (Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"), "Edge Cache", "Cache do Microsoft Edge"),
            (Path.Combine(local, "Mozilla", "Firefox", "Profiles"), "Firefox Cache", "Cache do Mozilla Firefox"),
            (Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"), "Brave Cache", "Cache do Brave"),
        };

        foreach (var (p, n, d) in browserPaths)
        {
            if (Directory.Exists(p))
            {
                list.AddRange(AnalyzePath(p, n, d, CleanupCategory.BrowserCache));
            }
            else if (p.Contains("Firefox") && Directory.Exists(Path.GetDirectoryName(p)))
            {
                try
                {
                    var profiles = Directory.GetDirectories(Path.GetDirectoryName(p)!);
                    foreach (var profile in profiles)
                    {
                        var cachePath = Path.Combine(profile, "cache2");
                        if (Directory.Exists(cachePath))
                            list.AddRange(AnalyzePath(cachePath, $"{n} ({Path.GetFileName(profile)})", d, CleanupCategory.BrowserCache));
                    }
                }
                catch { }
            }
        }
        return list;
    }

    public async Task<CleanupResult> CleanupAsync(IEnumerable<CleanupItem> items, IProgress<string>? progress = null)
    {
        return await Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            var result = new CleanupResult();
            long totalFreed = 0;
            int totalFiles = 0;

            foreach (var item in items.Where(i => i.IsSelected))
            {
                progress?.Report($"Limpando {item.Name}...");
                try
                {
                    long freed = 0;
                    int files = 0;

                    if (item.Category == CleanupCategory.RecycleBin)
                    {
                        await ClearRecycleBinAsync();
                        freed = item.SizeBytes;
                    }
                    else
                    {
                        (freed, files) = CleanDirectory(item.Path);
                    }

                    totalFreed += freed;
                    totalFiles += files;
                    result.CleanedItems.Add(item);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{item.Name}: {ex.Message}");
                }
            }

            result.TotalFreedBytes = totalFreed;
            result.TotalFilesDeleted = totalFiles;
            result.Duration = sw.Elapsed;

            await _log.LogAsync("Limpeza", "Limpeza", "SUCESSO", $"{CleanupItem.FormatBytes(totalFreed)} removidos, {totalFiles} arquivos", "", null, totalFreed);

            return result;
        });
    }

    private (long FreedBytes, int FileCount) CleanDirectory(string path, string pattern = "*")
    {
        long freed = 0;
        int count = 0;
        try
        {
            if (!Directory.Exists(path)) return (0, 0);
            var dir = new DirectoryInfo(path);
            var files = dir.GetFiles(pattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    if (file.LastWriteTime > DateTime.Now.AddDays(-1)) continue; // Safety
                    freed += file.Length;
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    count++;
                }
                catch { }
            }

            // Try to delete empty subdirectories
            foreach (var subDir in dir.GetDirectories("*", SearchOption.AllDirectories).OrderByDescending(d => d.FullName.Length))
            {
                try
                {
                    if (!subDir.GetFiles().Any() && !subDir.GetDirectories().Any())
                        subDir.Delete();
                }
                catch { }
            }
        }
        catch { }
        return (freed, count);
    }

    public async Task<long> GetRecycleBinSizeAsync()
    {
        return await Task.Run(() => GetRecycleBinSizeSync());
    }

    private long GetRecycleBinSizeSync()
    {
        long size = 0;
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var recyclePath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                if (Directory.Exists(recyclePath))
                {
                    var di = new DirectoryInfo(recyclePath);
                    try
                    {
                        size += di.GetFiles("*", SearchOption.AllDirectories).Sum(f => { try { return f.Length; } catch { return 0; } });
                    }
                    catch { }
                }
            }
        }
        catch { }
        return size;
    }

    public async Task ClearRecycleBinAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                // Use Shell32 via PowerShell to clear recycle bin safely
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);
            }
            catch { }
        });
    }
}

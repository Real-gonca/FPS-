using HL.Optimizer.Core.Models;
namespace HL.Optimizer.Core.Services;
/// <summary>Only explicitly configured temporary roots; never follows reparse points.</summary>
public sealed class CleanerService
{
    private readonly string[] roots;
    private readonly TimeSpan minimumAge;
    public CleanerService(IEnumerable<string> roots, TimeSpan? minimumAge = null)
    {
        this.roots = roots.Select(Path.GetFullPath).Select(p => Path.TrimEndingDirectorySeparator(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        this.minimumAge = minimumAge ?? TimeSpan.FromDays(7);
        if (this.roots.Any(p => p == Path.GetPathRoot(p))) throw new ArgumentException("A disk root is not a temporary folder.");
    }
    public bool IsAllowed(string path)
    {
        var full = Path.GetFullPath(path);
        var root = roots.FirstOrDefault(r => full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (root is null) return false;
        // Check every ancestor, including ancestors above the configured root.
        for (var current = new FileInfo(full).Directory; current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        return (File.GetAttributes(full) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0;
    }
    public Task<CleanScan> ScanAsync(CancellationToken token) => Task.Run(() =>
    {
        var files = new List<CleanCandidate>(); var skipped = new List<string>();
        foreach (var root in roots)
        {
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.TryPop(out var dir))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) { skipped.Add(dir); continue; }
                    foreach (var path in Directory.EnumerateFiles(dir))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var info = new FileInfo(path);
                            if (IsAllowed(path) && info.LastWriteTimeUtc < DateTime.UtcNow - minimumAge)
                                {
                                files.Add(new(path, info.Length, info.LastWriteTimeUtc, $"Temporary files · older than {minimumAge.TotalDays:g} days"));
                                if (files.Count >= 10000)
                                {
                                    skipped.Add("Analysis limited to 10,000 candidates to bound memory use. Run another analysis after cleaning.");
                                    return new CleanScan(files, skipped);
                                }
                            }
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped.Add(path + ": " + e.Message); }
                    }
                    foreach (var child in Directory.EnumerateDirectories(dir)) pending.Push(child);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped.Add(dir + ": " + e.Message); }
            }
        }
        return new CleanScan(files, skipped);
    }, token);
    public Task<CleanResult> CleanAsync(IEnumerable<CleanCandidate> selected, CancellationToken token) => Task.Run(() =>
    {
        long bytes = 0; int count = 0; var skipped = new List<string>();
        foreach (var item in selected)
        {
            if (token.IsCancellationRequested) { skipped.Add("Cancelled: remaining files were not deleted."); break; }
            try
            {
                var info = new FileInfo(item.Path);
                if (!IsAllowed(item.Path) || info.Length != item.Bytes || info.LastWriteTimeUtc != item.LastWriteUtc || info.LastWriteTimeUtc >= DateTime.UtcNow - minimumAge)
                { skipped.Add(item.Path + ": changed since scan or outside policy"); continue; }
                File.Delete(item.Path); bytes += item.Bytes; count++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped.Add(item.Path + ": " + e.Message); }
        }
        return new CleanResult(bytes, count, skipped);
    });
}

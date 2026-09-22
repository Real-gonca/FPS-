namespace HL.Optimizer.Pro.Core.Models;

public class CleanupItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public string SizeFormatted => FormatBytes(SizeBytes);
    public int FileCount { get; set; }
    public bool IsSelected { get; set; } = true;
    public CleanupCategory Category { get; set; }

    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public enum CleanupCategory
{
    TempWindows,
    TempUser,
    Cache,
    Logs,
    RecycleBin,
    BrowserCache,
    Thumbnails,
    WindowsUpdate,
    AppTemp,
    Other
}

public class CleanupResult
{
    public long TotalFreedBytes { get; set; }
    public int TotalFilesDeleted { get; set; }
    public List<CleanupItem> CleanedItems { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

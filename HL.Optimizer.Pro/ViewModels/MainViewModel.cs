using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using HL.Optimizer.Core.Interfaces;
using HL.Optimizer.Core.Models;
using HL.Optimizer.Core.Optimization;
using HL.Optimizer.Core.Services;
using HL.Optimizer.Infrastructure.Registry;
using HL.Optimizer.Infrastructure.WindowsAPI;
namespace HL.Optimizer.ViewModels;
public sealed class CleanItem(CleanCandidate candidate) : Observable
{
    private bool selected;
    public bool Selected { get => selected; set => Set(ref selected, value); }
    public CleanCandidate Candidate { get; } = candidate;
    public string Path => Candidate.Path;
    public string Size => $"{Candidate.Bytes / 1048576d:F2} MB";
    public string Category => Candidate.Category;
}
public sealed class MainViewModel : Observable, IDisposable
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HL.Optimizer.Pro");
    private readonly SystemProbe probe = new();
    private readonly JsonJournal journal = new(Path.Combine(DataDirectory, "History"));
    private readonly CleanerService cleaner = new([Path.GetTempPath()]);
    private readonly OptimizationRunner runner;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer;
    private readonly Func<string, bool> confirm;
    private bool sampling, busy;
    private int navigationVersion;
    private string selectedPage = "Dashboard", status = "Ready · no system changes applied", description = "";
    public ObservableCollection<Metric> Metrics { get; } = [];
    public ObservableCollection<InventoryRow> Rows { get; } = [];
    public ObservableCollection<CleanItem> Files { get; } = [];
    public ObservableCollection<string> Activity { get; } = [];
    public string[] Pages { get; } = ["Dashboard", "PC Health", "Cleaner", "Booster", "Gaming", "Tweaks", "Debloat", "Startup Manager", "Process Manager", "Hardware Monitor", "Network", "Storage", "Applications", "Windows Tools", "Restore Center", "Settings"];
    public IReadOnlyList<IReversibleSetting> Tweaks { get; } = UserSetting.Catalog();
    public IReadOnlyList<Tool> Tools => WindowsTools.Catalog;
    public IReversibleSetting? SelectedTweak { get; set; }
    public Tool? SelectedTool { get; set; }
    public int[] Intervals { get; } = [1, 2, 5, 10];
    private int interval = 2;
    public int Interval { get => interval; set { if (Intervals.Contains(value) && Set(ref interval, value)) timer.Interval = TimeSpan.FromSeconds(value); } }
    public bool IsIdle => !Busy;
    public bool Busy { get => busy; private set { if (Set(ref busy, value)) Changed(nameof(IsIdle)); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public string Description { get => description; private set => Set(ref description, value); }
    public bool ShowScan => SelectedPage is "Dashboard" or "PC Health";
    public bool ShowMetrics => SelectedPage is "Dashboard" or "Hardware Monitor";
    public bool ShowCleaner => SelectedPage == "Cleaner";
    public bool ShowTweaks => SelectedPage == "Tweaks";
    public bool ShowTools => SelectedPage == "Windows Tools";
    public bool ShowSettings => SelectedPage == "Settings";
    public bool ShowRows => !ShowCleaner && !ShowTweaks && !ShowTools && !ShowSettings;
    public string SelectedPage
    {
        get => selectedPage;
        set
        {
            if (!Set(ref selectedPage, value)) return;
            foreach (var p in new[] { nameof(ShowScan), nameof(ShowMetrics), nameof(ShowCleaner), nameof(ShowTweaks), nameof(ShowTools), nameof(ShowSettings), nameof(ShowRows) }) Changed(p);
            Refresh.Execute(null);
        }
    }
    public AsyncCommand Refresh { get; }
    public AsyncCommand Scan { get; }
    public AsyncCommand Clean { get; }
    public AsyncCommand Apply { get; }
    public AsyncCommand Undo { get; }
    public AsyncCommand LaunchTool { get; }
    public AsyncCommand Export { get; }
    public AsyncCommand SaveSettings { get; }
    public MainViewModel(Func<string, bool> confirm)
    {
        this.confirm = confirm; runner = new(journal);
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
        timer.Tick += OnTick;
        Refresh = Command(RefreshAsync);
        Scan = Command(async () =>
        {
            var findings = await new HealthService(probe).ScanAsync(lifetime.Token);
            Rows.Clear(); foreach (var f in findings) Rows.Add(new(f.Name, f.Priority + " · " + f.Result, f.Recommendation));
            Status = "Scan complete · sample-based checks only; no synthetic health score";
        });
        Clean = Command(async () =>
        {
            var selected = Files.Where(f => f.Selected).Select(f => f.Candidate).ToArray();
            if (selected.Length == 0) { Status = "Select individual files after analysis."; return; }
            if (!confirm($"Permanently delete {selected.Length} selected temporary files ({selected.Sum(f => f.Bytes) / 1048576d:F1} MB)? This cannot be undone. Close applications first. No browser profiles or personal folders are included.")) return;
            var id = Guid.NewGuid();
            await journal.AppendAsync(new(id, DateTimeOffset.Now, "cleaner", "Prepared", $"User consent: {selected.Length} files", JsonSerializer.Serialize(selected)));
            var result = await cleaner.CleanAsync(selected, lifetime.Token);
            await journal.AppendAsync(new(id, DateTimeOffset.Now, "cleaner", "Completed", JsonSerializer.Serialize(result)));
            Files.Clear(); Status = $"Deleted {result.Deleted} files · {result.Bytes / 1048576d:F1} MB freed · {result.Skipped.Count} skipped. Export history for details.";
        });
        Apply = Command(async () =>
        {
            if (SelectedTweak is null) { Status = "Select a setting."; return; }
            var task = SelectedTweak;
            if (!confirm(task.Definition.Title + "\n\n" + task.Definition.Description + "\nRisk: Low. A per-value backup is saved first. This is not a system restore point. Apply?"))
            { Status = "Cancelled · no setting changed."; return; }
            await runner.ApplyAsync(task, true, lifetime.Token);
            Status = "Setting applied and verified. Undo is available in Tweaks.";
        });
        Undo = Command(async () =>
        {
            if (SelectedTweak is null) { Status = "Select a setting to restore."; return; }
            if (!confirm("Restore the saved value for " + SelectedTweak.Definition.Title + "?")) return;
            await runner.UndoAsync(SelectedTweak, lifetime.Token); Status = "Previous value restored and verified.";
        });
        LaunchTool = Command(async () =>
        {
            if (SelectedTool is null) { Status = "Select a Windows tool."; return; }
            WindowsTools.Launch(SelectedTool);
            await journal.AppendAsync(new(Guid.NewGuid(), DateTimeOffset.Now, "windows-tool", "Launched", SelectedTool.Name));
            Status = "Opened in Windows · completion is not tracked by HL.";
        });
        Export = Command(async () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "HL-report.json", Filter = "JSON report|*.json" };
            if (dialog.ShowDialog() != true) return;
            var data = new { Generated = DateTimeOffset.Now, Page = SelectedPage, Metrics = Metrics.ToArray(), Results = Rows.ToArray(), History = await journal.ReadAsync(lifetime.Token) };
            await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), lifetime.Token);
            Status = "Report exported. Hardware names, paths and addresses may be personal information.";
        });
        SaveSettings = Command(async () =>
        {
            Directory.CreateDirectory(DataDirectory);
            var path = Path.Combine(DataDirectory, "settings.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new Preferences(Interval)), lifetime.Token);
            File.Move(path + ".tmp", path, true); Status = "Monitoring interval saved.";
        });
        try
        {
            var path = Path.Combine(DataDirectory, "settings.json");
            if (File.Exists(path)) Interval = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path))?.Interval ?? 2;
        }
        catch (Exception e) { Status = "Settings could not be loaded: " + e.Message; }
        SelectedTweak = Tweaks.FirstOrDefault(); SelectedTool = Tools.FirstOrDefault(); timer.Start();
    }
    private sealed record Preferences(int Interval);
    private AsyncCommand Command(Func<Task> action) => new(async () =>
    {
        if (Busy) { Status = "Another operation is running. Please wait."; return; }
        Busy = true;
        try { await action(); Activity.Insert(0, $"{DateTime.Now:HH:mm:ss} · {Status}"); while (Activity.Count > 40) Activity.RemoveAt(Activity.Count - 1); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            try { await journal.AppendAsync(new(Guid.NewGuid(), DateTimeOffset.Now, SelectedPage, "Failed", error.Message)); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            { Activity.Insert(0, "Could not persist error log: " + logError.Message); }
            throw;
        }
        finally { Busy = false; }
    }, e => { Status = "Operation failed: " + e.Message; Activity.Insert(0, Status); });
    private async void OnTick(object? sender, EventArgs e)
    {
        if (sampling || !ShowMetrics || Busy) return;
        sampling = true;
        try { await SampleAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = "Monitoring unavailable: " + ex.Message; }
        finally { sampling = false; }
    }
    private async Task SampleAsync()
    {
        var metrics = await probe.ReadAsync(lifetime.Token);
        Metrics.Clear(); foreach (var m in metrics) Metrics.Add(m);
    }
    private async Task RefreshAsync()
    {
        var page = SelectedPage; var version = ++navigationVersion; Rows.Clear();
        Description = page switch
        {
            "Dashboard" => "Your system, measured. Live samples — no invented performance gains.",
            "Hardware Monitor" => "Windows API sampling. GPU, temperature, power, SMART and motherboard providers are not integrated yet; unavailable values are N/D.",
            "PC Health" => "Quick Scan checks CPU, memory and system-volume pressure. Full and Gaming Readiness scans are not implemented yet.",
            "Cleaner" => "Analyze user temporary files older than seven days. Nothing is selected automatically. No browser data, Downloads or game saves. Close apps before cleaning.",
            "Tweaks" => "Two current-user preferences. Write-ahead backups, verification and individual Undo. No essential services are changed.",
            "Gaming" => "Detects running launcher/emulator process names only; not a complete installed-game inventory. No suspensions, FPS promises or driver changes.",
            "Booster" => "Read-only memory working-set inventory. Automatic boost profiles and service changes are not implemented; no artificial RAM purges.",
            "Debloat" => "Read-only desktop-app inventory from uninstall keys. Store apps and dependency-aware removal are not implemented. Use Windows Installed apps to uninstall.",
            "Network" => "Current adapter configuration. DNS comparison, modification and network resets are not implemented; use Windows Network settings.",
            "Restore Center" => "Persistent operation history, including prepared backups. To recover a pending registry change, select it in Tweaks and use Undo. Windows restore points are managed in Windows Tools.",
            "Settings" => "Monitoring preferences are stored locally. Current build: English UI, dark theme. Other languages, themes, updater and autostart are not implemented.",
            "Storage" => "Real volume capacity and free space. Physical-drive mapping, SMART, large-file analysis and USB notifications are not implemented yet.",
            "Windows Tools" => "Allowlisted Windows tools launch externally. HL does not claim completion or collect their progress. Windows may request elevation.",
            _ => "Read-only inventory. Refresh to rescan; export to save the current results."
        };
        if (ShowMetrics) { await SampleAsync(); Rows.Add(new("Measurement scope", "CPU / memory / system volume", "Select Scan PC for sample-based pressure checks. Sensor history graphs are not yet implemented.")); }
        else if (page == "PC Health") Rows.Add(new("Ready to scan", "No scan performed", "Use Scan PC. No changes are applied by a scan."));
        else if (page == "Cleaner")
        {
            Files.Clear(); Status = "Analyzing temporary files…"; var scan = await cleaner.ScanAsync(lifetime.Token);
            if (version != navigationVersion || SelectedPage != page) return;
            Files.Clear(); foreach (var f in scan.Files) Files.Add(new(f));
            Status = $"{scan.Files.Count} candidates · {scan.Files.Sum(f => f.Bytes) / 1048576d:F1} MB · {scan.Skipped.Count} inaccessible paths skipped. No files selected.";
            return;
        }
        else if (page == "Restore Center")
        {
            var history = await journal.ReadAsync(lifetime.Token);
            foreach (var entry in history.Reverse()) Rows.Add(new(entry.TaskId, entry.Status + " · " + entry.Time.ToString("g"), entry.Detail));
        }
        else if (page is not ("Settings" or "Tweaks" or "Windows Tools"))
        {
            var rows = await probe.InventoryAsync(page, lifetime.Token);
            if (SelectedPage != page) return;
            foreach (var row in rows) Rows.Add(row);
        }
        if (ShowRows && Rows.Count == 0) Rows.Add(new("No results", "Nothing returned by this provider", "This is not a guarantee that the computer has no installed applications, games or issues."));
        Status = "Updated " + DateTime.Now.ToString("HH:mm:ss") + " · no system changes";
    }
    public void Dispose() { timer.Stop(); timer.Tick -= OnTick; lifetime.Cancel(); }
}

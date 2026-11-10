using HL.Optimizer.Core.Interfaces;
using HL.Optimizer.Core.Models;
using HL.Optimizer.Core.Services;
using HL.Optimizer.Core.Optimization;
var root = Path.Combine(Path.GetTempPath(), "hl-tests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string title) { if (!condition) throw new Exception(title); Console.WriteLine("PASS " + title); passed++; }
async Task Throws(Func<Task> operation, string title) { try { await operation(); } catch { Check(true, title); return; } throw new Exception("Expected failure: " + title); }
try
{
    var temp = Path.Combine(root, "temp"); Directory.CreateDirectory(temp);
    var old = Path.Combine(temp, "old.tmp"); await File.WriteAllTextAsync(old, "old cache"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));
    var recent = Path.Combine(temp, "recent.tmp"); await File.WriteAllTextAsync(recent, "keep");
    var personal = Path.Combine(root, "document.txt"); await File.WriteAllTextAsync(personal, "keep me");
    var cleaner = new CleanerService([temp]);
    var scan = await cleaner.ScanAsync(default);
    Check(scan.Files.Count == 1 && scan.Files[0].Path == old, "Cleaner only includes old files");
    Check(!cleaner.IsAllowed(personal), "Cleaner rejects paths outside allowlist");
    var result = await cleaner.CleanAsync([new(personal, 7, File.GetLastWriteTimeUtc(personal), "forged")], default);
    Check(result.Deleted == 0 && File.Exists(personal), "Forged candidate cannot delete a personal file");
    await File.WriteAllTextAsync(old, "modified after analysis");
    result = await cleaner.CleanAsync(scan.Files, default);
    Check(result.Deleted == 0 && File.Exists(old), "Changed candidate is skipped");
    File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));
    scan = await cleaner.ScanAsync(default); result = await cleaner.CleanAsync(scan.Files, default);
    Check(result.Deleted == 1 && !File.Exists(old) && File.Exists(recent), "Selected old cache deleted, recent file retained");
    var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
    var secret = Path.Combine(outside, "secret.tmp"); await File.WriteAllTextAsync(secret, "private"); File.SetLastWriteTimeUtc(secret, DateTime.UtcNow.AddDays(-10));
    try
    {
        Directory.CreateSymbolicLink(Path.Combine(temp, "link"), outside);
        Check(!(await cleaner.ScanAsync(default)).Files.Any(f => f.Path.Contains("link")), "Scanner does not follow directory symlinks");
    }
    catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
    { Console.WriteLine("SKIP symlink test: enable Windows Developer Mode or run in an elevated test VM."); }
    var cancelled = new CancellationToken(true);
    await Throws(async () => { await cleaner.ScanAsync(cancelled); }, "Scan supports cancellation");
    var journal = new JsonJournal(Path.Combine(root, "journal"));
    var runner = new OptimizationRunner(journal); var setting = new FakeSetting();
    await Throws(() => runner.ApplyAsync(setting, false, default), "Explicit consent required");
    Check(setting.Value == "original", "No write without consent");
    await runner.ApplyAsync(setting, true, default);
    Check(setting.Value == "target", "Apply changes setting");
    var history = await journal.ReadAsync(); Check(history[0].Status == "Prepared" && history[0].Before == "original" && history[1].Status == "Applied", "Backup precedes verified apply");
    await Throws(() => runner.ApplyAsync(setting, true, default), "Repeated apply preserves original backup");
    setting.Value = "external";
    await Throws(() => runner.UndoAsync(setting, default), "External changes prevent blind restore");
    setting.Value = "target"; await runner.UndoAsync(setting, default);
    Check(setting.Value == "original", "Individual undo restores original");
    setting.FailTarget = true;
    await Throws(() => runner.ApplyAsync(setting, true, default), "Apply failure reported");
    Check(setting.Value == "original" && (await journal.ReadAsync()).Last().Status == "Restored", "Apply failure compensates and journals restore");
    setting.FailTarget = false;
    var pending = new OperationRecord(Guid.NewGuid(), DateTimeOffset.Now, "fake", "Prepared", "interrupted", "original");
    await journal.AppendAsync(pending); setting.Value = "target";
    await new OptimizationRunner(new JsonJournal(Path.Combine(root, "journal"))).UndoAsync(setting, default);
    Check(setting.Value == "original", "Pending write-ahead backup recoverable after restart");
    var findings = await new HealthService(new FakeProbe()).ScanAsync(default);
    Check(findings.Any(f => f.Priority == "Attention") && findings.Any(f => f.Name == "Scan coverage"), "Health uses measured thresholds and declares coverage");
    await Task.WhenAll(Enumerable.Range(0, 20).Select(i => journal.AppendAsync(new(Guid.NewGuid(), DateTimeOffset.Now, "parallel", "Completed", i.ToString()))));
    Check((await journal.ReadAsync()).Count(x => x.TaskId == "parallel") == 20, "Concurrent journal writes retain all records");
    Console.WriteLine($"{passed} core checks passed.");
}
finally { Directory.Delete(root, true); }
sealed class FakeSetting : IReversibleSetting
{
    public string Value = "original";
    public bool FailTarget;
    public TaskDefinition Definition => new("fake", "Fake", "Test", "Registry", RiskLevel.Low, "Test", "None", true);
    public string Target => "target";
    public string Read() => Value;
    public void Write(string value) { Value = value; if (FailTarget && value == Target) throw new IOException("Injected write failure"); }
}
sealed class FakeProbe : ISystemProbe
{
    public Task<IReadOnlyList<Metric>> ReadAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<Metric>>([new("RAM", "7.6 GB", "95%", 95), new("GPU", "N/D", "Unavailable")]);
    public Task<IReadOnlyList<InventoryRow>> InventoryAsync(string category, CancellationToken token) => Task.FromResult<IReadOnlyList<InventoryRow>>([]);
}

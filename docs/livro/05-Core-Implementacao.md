# Capítulo 5: Implementando o Core - Coração do Sistema

## 5.1 Models - Estruturas de Dados

### SystemInfo.cs
```csharp
public class SystemInfo
{
    public string ComputerName { get; set; } = Environment.MachineName;
    public string OsName { get; set; } = "Windows";
    public string CpuName { get; set; } = "";
    public int CpuCores { get; set; }
    public long TotalRamBytes { get; set; }
    public List<DiskInfo> Disks { get; set; } = new();
}
public class DiskInfo
{
    public string Name { get; set; } = "";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public double FreePercent => TotalBytes > 0 ? (double)FreeBytes/TotalBytes*100 : 0;
}
```

Crie também: PerformanceMetrics, OptimizationItem (com Id, Name, Category, Risk, ExecuteAction Func<Task<>>), TweakItem, CleanupItem, GameProfile, EmulatorInfo, StartupItem, LogEntry, DiagnosisResult.

### OptimizationItem - O Mais Importante
```csharp
public class OptimizationItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public OptimizationCategory Category { get; set; }
    public OptimizationRisk Risk { get; set; } = OptimizationRisk.Seguro;
    public bool Reversible { get; set; } = true;
    public bool RequiresAdmin { get; set; }
    public Func<Task<OptimizationResult>>? ExecuteAction { get; set; }
    public Func<Task<OptimizationResult>>? RevertAction { get; set; }
}
```

## 5.2 Interfaces - Contratos

`IServices.cs` com todas as interfaces. Exemplo:

```csharp
public interface ISystemInfoService
{
    Task<SystemInfo> GetSystemInfoAsync();
}
public interface IOptimizationService
{
    Task<List<OptimizationItem>> GetAvailableOptimizationsAsync();
    Task<OptimizationResult> ExecuteOptimizationAsync(OptimizationItem item);
}
```

## 5.3 Utilities - Helpers

### WmiHelper
```csharp
public static List<ManagementBaseObject> Query(string wql)
{
    var result = new List<ManagementBaseObject>();
    using var searcher = new ManagementObjectSearcher(@"root\cimv2", wql);
    foreach (ManagementBaseObject obj in searcher.Get()) result.Add(obj);
    return result;
}
```

### RegistryHelper
```csharp
public static bool SetValue(string keyPath, string valueName, object value, RegistryValueKind kind, RegistryHive hive)
{
    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
    using var key = baseKey.CreateSubKey(keyPath, true);
    key.SetValue(valueName, value, kind);
    return true;
}
```

### AdminHelper, PowerShellHelper, NativeMethods, Converters

## 5.4 Services - Lógica Real

### SystemInfoService
```csharp
public async Task<SystemInfo> GetSystemInfoAsync()
{
    return await Task.Run(() =>
    {
        var info = new SystemInfo();
        var osQuery = WmiHelper.Query("SELECT Caption, Version FROM Win32_OperatingSystem");
        if (osQuery.Count > 0) info.OsName = WmiHelper.GetPropertyString(osQuery[0], "Caption");
        var cpuQuery = WmiHelper.Query("SELECT Name, NumberOfCores FROM Win32_Processor");
        // ... etc
        foreach (var o in osQuery) o.Dispose();
        return info;
    });
}
```

### PerformanceMonitorService
```csharp
public class PerformanceMonitorService : IPerformanceMonitorService
{
    private PerformanceCounter? _cpuCounter;
    private Timer? _timer;
    public event EventHandler<PerformanceMetrics>? MetricsUpdated;

    public void StartMonitoring(int intervalMs = 1000)
    {
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _cpuCounter.NextValue(); Thread.Sleep(500);
        _timer = new Timer(OnTimer, null, 0, intervalMs);
    }
    private void OnTimer(object? state)
    {
        var metrics = new PerformanceMetrics { CpuUsage = _cpuCounter.NextValue() };
        // RAM via GlobalMemoryStatusEx, etc.
        MetricsUpdated?.Invoke(this, metrics);
    }
}
```

### OptimizationService
Lista de otimizações com ações reais:
```csharp
items.Add(new OptimizationItem
{
    Id = "power_highperf",
    Name = "Ativar plano Alto Desempenho",
    Category = OptimizationCategory.Energia,
    ExecuteAction = async () => { await _power.SetHighPerformanceAsync(); return new OptimizationResult { Success = true }; }
});
```

### CleanupService
```csharp
public async Task<List<CleanupItem>> AnalyzeAsync()
{
    var items = new List<CleanupItem>();
    items.AddRange(AnalyzePath(Path.GetTempPath(), "Temp Usuário", ...));
    // Browser caches, Recycle Bin, etc.
    return items;
}
private (long, int) CleanDirectory(string path)
{
    long freed = 0; int count = 0;
    foreach (var file in new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories))
    {
        if (file.LastWriteTime > DateTime.Now.AddDays(-1)) continue; // segurança
        freed += file.Length; file.Delete(); count++;
    }
    return (freed, count);
}
```

### Outros Services
- StartupService: Registry Run + Startup Folder + Task Scheduler
- NetworkService: NetworkInterface.GetAllNetworkInterfaces, Ping, netsh
- PowerService: powercfg /list, /getactivescheme, /setactive
- GameDetectionService: Registry Uninstall + file scan Steam
- EmulatorDetectionService: paths conhecidos BlueStacks etc.
- RestoreService: WMI SystemRestore CreateRestorePoint + rstrui.exe
- LogService: SQLite + fallback arquivo
- DiagnosisService: checks disco, RAM, drivers, rede

Cada serviço deve ter try/catch e fallback.

Próximo: ViewModels e Views.

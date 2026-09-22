# Capítulo 7: Implementando os 12 Módulos

## 7.1 DashboardViewModel - O Cérebro

```csharp
public partial class DashboardViewModel : BaseViewModel
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IOptimizationService _optimization;

    [ObservableProperty] private SystemInfo? _sysInfo;
    [ObservableProperty] private OptimizationIndex? _optIndex;
    [ObservableProperty] private bool _isOptimizing;
    [ObservableProperty] private double _optimizationProgress;

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        SysInfo = await _systemInfo.GetSystemInfoAsync();
        OptIndex = await _optimization.CalculateOptimizationIndexAsync();
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ExecuteOptimization()
    {
        // 1. Perguntar ponto de restauração
        var result = MessageBox.Show("Criar ponto de restauração?", "HL Optimizer", MessageBoxButton.YesNoCancel);
        if (result == MessageBoxResult.Cancel) return;
        if (result == MessageBoxResult.Yes)
        {
            await _restore.CreateRestorePointAsync($"HL Optimizer {DateTime.Now}");
        }

        // 2. Executar
        IsOptimizing = true;
        var selected = Optimizations.Where(o => o.IsSelected).ToList();
        var progress = new Progress<OptimizationProgress>(p => OptimizationProgress = p.Percentage);
        var results = await _optimization.ExecuteOptimizationsAsync(selected, progress);
        IsOptimizing = false;
    }
}
```

Índice transparente:
```csharp
public async Task<OptimizationIndex> CalculateOptimizationIndexAsync()
{
    var items = await GetAvailableOptimizationsAsync();
    int completed = 0;
    foreach (var item in items) if (await IsAppliedAsync(item)) completed++;
    return new OptimizationIndex { TotalOptimizations = items.Count, CompletedOptimizations = completed };
}
```

## 7.2 Booster - 3 Modos

ViewModel:
```csharp
[RelayCommand]
private async Task ApplyMode(string mode)
{
    SelectedMode = mode;
    switch (mode)
    {
        case "Econômico": await _power.SetPowerSavingAsync(); break;
        case "Equilibrado": await _power.SetBalancedAsync(); break;
        case "Desempenho": await _power.SetHighPerformanceAsync(); break;
    }
    ActivePowerPlan = await _power.GetActivePowerPlanAsync();
}
```

PowerService:
```csharp
public async Task SetPowerPlanAsync(string guid)
{
    var psi = new ProcessStartInfo { FileName = "powercfg", Arguments = $"/setactive {guid}", UseShellExecute = false, CreateNoWindow = true };
    Process.Start(psi)?.WaitForExit(5000);
}
public string HighPerformanceGuid => "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
```

## 7.3 Tweaks - 8 Categorias

LoadTweaks:
```csharp
private async Task<List<TweakItem>> LoadTweaksAsync()
{
    return new List<TweakItem>
    {
        new() { Id="tweak_anim", Name="Desativar animações", Category=Interface, RecommendedState="Desativado", CurrentState="Ativo", Risk=Seguro },
        new() { Id="tweak_menudelay", Name="Acelerar menus", Category=Interface, RecommendedState="0ms", CurrentState="400ms" },
        // ... 14 tweaks
    };
}
```

Filtro:
```csharp
partial void OnSelectedCategoryChanged(string value) => Filter();
private void Filter()
{
    var query = AllTweaks.AsEnumerable();
    if (SelectedCategory != "Todos") query = query.Where(t => t.Category.ToString() == SelectedCategory);
    if (!string.IsNullOrWhiteSpace(SearchQuery)) query = query.Where(t => t.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
    FilteredTweaks = new ObservableCollection<TweakItem>(query);
}
```

## 7.4 Limpeza - Análise Real

```csharp
public async Task<List<CleanupItem>> AnalyzeAsync()
{
    var items = new List<CleanupItem>();
    items.AddRange(AnalyzePath(Path.GetTempPath(), "Temp Usuário", ...));
    items.AddRange(AnalyzePath(Path.Combine(Windows, "Temp"), "Temp Windows", ...));
    items.AddRange(AnalyzePath(Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer"), "Thumbnails", ..., "thumbcache_*.db"));
    // Browser caches
    items.AddRange(AnalyzePath(Path.Combine(LocalAppData, "Google", "Chrome", "User Data", "Default", "Cache"), "Chrome Cache", ...));
    // Recycle Bin
    var recycleSize = GetRecycleBinSizeSync();
    if (recycleSize > 0) items.Add(new CleanupItem { Name="Lixeira", SizeBytes=recycleSize, Category=RecycleBin });
    return items.Where(i => i.SizeBytes > 0).ToList();
}

private List<CleanupItem> AnalyzePath(string path, string name, string desc, CleanupCategory cat, string pattern="*")
{
    if (!Directory.Exists(path)) return new List<CleanupItem>();
    var di = new DirectoryInfo(path);
    long size = 0; int count = 0;
    try
    {
        var files = di.GetFiles(pattern, SearchOption.AllDirectories);
        foreach (var file in files)
        {
            if (file.LastWriteTime > DateTime.Now.AddDays(-1)) continue; // segurança
            size += file.Length; count++;
        }
    }
    catch {}
    if (size > 0) return new List<CleanupItem> { new() { Name=name, Description=desc, Path=path, SizeBytes=size, FileCount=count, Category=cat } };
    return new List<CleanupItem>();
}
```

Limpeza:
```csharp
public async Task<CleanupResult> CleanupAsync(IEnumerable<CleanupItem> items)
{
    long totalFreed = 0; int totalFiles = 0;
    foreach (var item in items.Where(i => i.IsSelected))
    {
        if (item.Category == RecycleBin) await ClearRecycleBinAsync();
        else { var (freed, files) = CleanDirectory(item.Path); totalFreed += freed; totalFiles += files; }
    }
    return new CleanupResult { TotalFreedBytes=totalFreed, TotalFilesDeleted=totalFiles };
}
```

## 7.5 Jogos - Detecção Real

```csharp
public async Task<List<GameProfile>> DetectGamesAsync()
{
    var games = new List<GameProfile>();
    // Registry Uninstall
    var uninstallKeys = new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };
    foreach (var keyPath in uninstallKeys)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        using var key = baseKey.OpenSubKey(keyPath);
        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName);
            var displayName = subKey.GetValue("DisplayName")?.ToString() ?? "";
            if (displayName.ToLower().Contains("valorant")) games.Add(new GameProfile { Name=displayName, Type=Valorant });
        }
    }
    // File system Steam
    var steamPath = Path.Combine(ProgramFilesX86, "Steam", "steamapps", "common");
    if (Directory.Exists(steamPath))
    {
        foreach (var dir in Directory.GetDirectories(steamPath))
        {
            var exe = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe != null) games.Add(new GameProfile { Name=Path.GetFileName(dir), ExecutablePath=exe });
        }
    }
    return games;
}
```

Game Booster:
```csharp
public async Task<bool> ApplyGameBoosterAsync(GameProfile game)
{
    if (game.BoosterSettings.HighPerformancePowerPlan) await _power.SetHighPerformanceAsync();
    var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(game.ExecutablePath));
    foreach (var proc in procs) { try { proc.PriorityClass = ProcessPriorityClass.High; } catch {} }
    // Suspender não essenciais
    foreach (var name in new[] { "onedrive", "searchindexer" })
    {
        foreach (var p in Process.GetProcessesByName(name)) { try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch {} }
    }
    return true;
}
```

## 7.6 Emuladores

Paths conhecidos:
```csharp
var emulatorPaths = new Dictionary<EmulatorType, string[]>
{
    { BlueStacks, new[] { @"C:\Program Files\BlueStacks_nxt", @"C:\Program Files\BlueStacks" } },
    { LDPlayer, new[] { @"C:\LDPlayer", @"C:\Program Files\LDPlayer" } },
    // ...
};
```

Detecta via Directory.Exists + GetFiles exe + Process.GetProcesses.

Apply profile com backup:
```csharp
var configPath = Path.Combine(CommonApplicationData, "BlueStacks_nxt", "bluestacks.conf");
if (File.Exists(configPath)) File.Copy(configPath, configPath + ".backup", true);
// Não modifica sem confirmação do usuário - segurança
```

## 7.7 Inicialização

```csharp
public async Task<List<StartupItem>> GetStartupItemsAsync()
{
    var items = new List<StartupItem>();
    items.AddRange(GetRegistryStartupItems(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", RegistryHive.CurrentUser, RegistryCurrentUser));
    items.AddRange(GetRegistryStartupItems(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", RegistryHive.LocalMachine, RegistryLocalMachine));
    items.AddRange(GetStartupFolderItems()); // Environment.SpecialFolder.Startup
    return items;
}

private bool IsStartupEnabled(string name, RegistryHive hive)
{
    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
    using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
    var data = key?.GetValue(name) as byte[];
    if (data != null && data.Length > 0) return data[0] == 2; // 2=enabled, 3=disabled
    return true;
}

public async Task<bool> SetStartupItemEnabledAsync(StartupItem item, bool enabled)
{
    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
    using var key = baseKey.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true);
    byte[] data = enabled ? new byte[] { 2,0,0,0,0,0,0,0,0,0,0,0 } : new byte[] { 3,0,0,0,0,0,0,0,0,0,0,0 };
    key.SetValue(item.Name, data, RegistryValueKind.Binary);
    return true;
}
```

## 7.8 Monitor

```csharp
public class PerformanceMonitorService
{
    private Timer? _timer;
    public void StartMonitoring(int intervalMs = 1000)
    {
        InitializeCounters();
        _timer = new Timer(OnTimer, null, 0, intervalMs);
    }
    private void OnTimer(object? state)
    {
        var metrics = new PerformanceMetrics
        {
            CpuUsage = _cpuCounter.NextValue(),
            RamUsage = CalculateRamUsage(),
            DiskReadMBps = _diskReadCounter.NextValue() / (1024*1024),
            // ...
        };
        MetricsUpdated?.Invoke(this, metrics);
        History.Add(metrics);
    }
}
```

View mostra ItemsControl de barras com Height binding para CpuUsage.

## 7.9 Sistema

Simples: chama SystemInfoService e mostra em cards.

## 7.10 Diagnóstico

```csharp
public async Task<List<DiagnosisResult>> RunDiagnosisAsync()
{
    var results = new List<DiagnosisResult>();
    results.Add(await CheckDiskHealthAsync());
    results.Add(await CheckMemoryAsync());
    results.Add(await CheckNetworkAsync());
    // ...
    return results;
}

public async Task<DiagnosisResult> CheckDiskHealthAsync()
{
    var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
    foreach (var drive in drives)
    {
        if (drive.TotalFreeSpace < 5L*1024*1024*1024)
            return new DiagnosisResult { Title="Pouco espaço", Severity=Warning, Problem=$"Unidade {drive.Name} com apenas {drive.TotalFreeSpace/(1024*1024*1024)}GB" };
    }
    // SMART via WMI Win32_DiskDrive Status != OK
    return new DiagnosisResult { Title="Disco saudável", Severity=Info };
}
```

## 7.11 Rede

```csharp
public async Task<NetworkInfo> GetNetworkInfoAsync()
{
    var interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == Up && n.NetworkInterfaceType != Loopback);
    var active = interfaces.FirstOrDefault();
    var props = active.GetIPProperties();
    var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == InterNetwork);
    return new NetworkInfo { IpAddress=ipv4.Address.ToString(), Gateway=props.GatewayAddresses.FirstOrDefault()?.Address.ToString(), DnsPrimary=props.DnsAddresses.FirstOrDefault()?.ToString() };
}

public async Task FlushDnsAsync()
{
    var psi = new ProcessStartInfo { FileName="ipconfig", Arguments="/flushdns", UseShellExecute=false, CreateNoWindow=true };
    Process.Start(psi)?.WaitForExit(5000);
}

public async Task SetDnsAsync(string primary, string secondary)
{
    var ni = NetworkInterface.GetAllNetworkInterfaces().First(n => n.OperationalStatus == Up);
    var psi = new ProcessStartInfo { FileName="netsh", Arguments=$"interface ip set dns \"{ni.Name}\" static {primary}", UseShellExecute=false, CreateNoWindow=true };
    Process.Start(psi)?.WaitForExit();
}
```

## 7.12 Ferramentas

```csharp
[RelayCommand] private void OpenTaskManager() => Launch("taskmgr.exe");
private void Launch(string file)
{
    Process.Start(new ProcessStartInfo { FileName=file, UseShellExecute=true });
}
```

Lista de 13 ferramentas: taskmgr, devmgmt.msc, diskmgmt.msc, eventvwr.msc, services.msc, msconfig, regedit, control, powershell, cmd, msinfo32, resmon, taskschd.msc

## 7.13 Restauração

```csharp
public async Task<bool> CreateRestorePointAsync(string description)
{
    try
    {
        using var mClass = new ManagementClass(@"\\.\\root\\default:SystemRestore");
        using var mMethod = mClass.GetMethodParameters("CreateRestorePoint");
        mMethod["Description"] = description;
        mMethod["RestorePointType"] = 12; // MODIFY_SETTINGS
        mMethod["EventType"] = 100;
        using var result = mClass.InvokeMethod("CreateRestorePoint", mMethod, null);
        return (uint)result["ReturnValue"] == 0;
    }
    catch
    {
        // Fallback PowerShell
        var psi = new ProcessStartInfo { FileName="powershell.exe", Arguments=$"-NoProfile -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType MODIFY_SETTINGS\"", UseShellExecute=false, CreateNoWindow=true };
        using var proc = Process.Start(psi); proc?.WaitForExit(30000); return proc?.ExitCode == 0;
    }
}

public void OpenSystemRestore() => Process.Start(new ProcessStartInfo { FileName="rstrui.exe", UseShellExecute=true });
```

## 7.14 Configurações e Logs

LogService com SQLite:
```csharp
public void Initialize()
{
    var appData = Path.Combine(LocalApplicationData, "HL Optimizer Pro");
    Directory.CreateDirectory(appData);
    _dbPath = Path.Combine(appData, "logs.db");
    using var conn = new SqliteConnection($"Data Source={_dbPath}");
    conn.Open();
    conn.CreateCommand().CommandText = "CREATE TABLE IF NOT EXISTS Logs (Id INTEGER PRIMARY KEY, Timestamp TEXT, Action TEXT, Result TEXT)".ExecuteNonQuery();
}
```

Próximo capítulo: segurança.

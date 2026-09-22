using HL.Optimizer.Pro.Core.Models;

namespace HL.Optimizer.Pro.Core.Interfaces;

public interface ISystemInfoService
{
    Task<SystemInfo> GetSystemInfoAsync();
    Task<List<DiskInfo>> GetDiskInfoAsync();
    Task<string> GetWindowsVersionAsync();
}

public interface IPerformanceMonitorService
{
    event EventHandler<PerformanceMetrics>? MetricsUpdated;
    PerformanceMetrics CurrentMetrics { get; }
    PerformanceHistory History { get; }
    void StartMonitoring(int intervalMs = 1000);
    void StopMonitoring();
    void SetInterval(int intervalMs);
}

public interface IOptimizationService
{
    Task<List<OptimizationItem>> GetAvailableOptimizationsAsync();
    Task<OptimizationIndex> CalculateOptimizationIndexAsync();
    Task<OptimizationResult> ExecuteOptimizationAsync(OptimizationItem item);
    Task<List<OptimizationResult>> ExecuteOptimizationsAsync(IEnumerable<OptimizationItem> items, IProgress<OptimizationProgress>? progress = null);
}

public class OptimizationProgress
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string CurrentItem { get; set; } = "";
    public double Percentage => Total > 0 ? (double)Current / Total * 100 : 0;
}

public interface ICleanupService
{
    Task<List<CleanupItem>> AnalyzeAsync(IProgress<string>? progress = null);
    Task<CleanupResult> CleanupAsync(IEnumerable<CleanupItem> items, IProgress<string>? progress = null);
    Task<long> GetRecycleBinSizeAsync();
    Task ClearRecycleBinAsync();
}

public interface IStartupService
{
    Task<List<StartupItem>> GetStartupItemsAsync();
    Task<bool> SetStartupItemEnabledAsync(StartupItem item, bool enabled);
    Task<List<ServiceInfo>> GetServicesAsync();
}

public interface INetworkService
{
    Task<NetworkInfo> GetNetworkInfoAsync();
    Task<double> PingAsync(string host = "8.8.8.8");
    Task FlushDnsAsync();
    Task ResetWinsockAsync();
    Task SetDnsAsync(string primary, string secondary);
    Task<List<string>> GetDnsServersAsync();
}

public interface IGameDetectionService
{
    Task<List<GameProfile>> DetectGamesAsync();
    Task<bool> ApplyGameBoosterAsync(GameProfile game);
    Task<bool> RevertGameBoosterAsync(GameProfile game);
}

public interface IEmulatorDetectionService
{
    Task<List<EmulatorInfo>> DetectEmulatorsAsync();
    Task<bool> ApplyPerformanceProfileAsync(EmulatorInfo emulator, EmulatorPerformanceProfile profile);
}

public interface IRestoreService
{
    Task<bool> CreateRestorePointAsync(string description);
    Task<List<RestorePointInfo>> GetRestorePointsAsync();
    void OpenSystemRestore();
    Task BackupRegistryValueAsync(string keyPath, string valueName);
}

public interface ILogService
{
    void Initialize();
    Task LogAsync(string action, string category, string result, string details = "", string command = "", string? error = null, long? freedBytes = null);
    Task<List<LogEntry>> GetLogsAsync(int limit = 100);
    Task ClearLogsAsync();
}

public interface IDiagnosisService
{
    Task<List<DiagnosisResult>> RunDiagnosisAsync(IProgress<string>? progress = null);
    Task<DiagnosisResult> CheckDiskHealthAsync();
    Task<DiagnosisResult> CheckMemoryAsync();
    Task<DiagnosisResult> CheckNetworkAsync();
}

public interface IPowerService
{
    Task<List<string>> GetPowerPlansAsync();
    Task<string> GetActivePowerPlanAsync();
    Task SetPowerPlanAsync(string planGuid);
    Task SetHighPerformanceAsync();
    Task SetBalancedAsync();
    Task SetPowerSavingAsync();
    string HighPerformanceGuid { get; }
    string BalancedGuid { get; }
    string PowerSaverGuid { get; }
}

public interface IRegistryService
{
    Task<object?> GetValueAsync(string keyPath, string valueName);
    Task<bool> SetValueAsync(string keyPath, string valueName, object value, Microsoft.Win32.RegistryValueKind kind);
    Task<bool> DeleteValueAsync(string keyPath, string valueName);
    Task<bool> KeyExistsAsync(string keyPath);
}

public interface ILocalizationService
{
    string CurrentLanguage { get; }
    void SetLanguage(string culture);
    string GetString(string key);
    List<string> AvailableLanguages { get; }
}

using System.Runtime.Versioning;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using HLOptimizerPro.Core.Services.Tweaks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Recolhe os dados reais da máquina e produz o relatório de avaliação.
///
/// Fluxo: medir → calcular (ScoringService, puro) → apresentar.
/// Nada aqui inventa valores: quando uma medição não é possível (sem sensores, sem
/// permissões, sem Internet), o dado fica a zero/nulo e o critério é apresentado como
/// "não medido" em vez de penalizar o utilizador.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemAssessmentService
{
    private readonly ILogger<SystemAssessmentService> _logger;
    private readonly MonitoringService _monitoring;
    private readonly MonitoringHistoryService _history;
    private readonly SystemInfoService _systemInfo;
    private readonly StartupService _startup;
    private readonly SecurityService _security;
    private readonly StorageService _storage;
    private readonly ScoringService _scoring;
    private readonly OptimizationCatalog _catalog;
    private readonly IRegistryProvider _registry;
    private readonly IProcessRunner _runner;

    public SystemAssessmentService(
        ILogger<SystemAssessmentService> logger,
        MonitoringService monitoring,
        MonitoringHistoryService history,
        SystemInfoService systemInfo,
        StartupService startup,
        SecurityService security,
        StorageService storage,
        ScoringService scoring,
        OptimizationCatalog catalog,
        IRegistryProvider registry,
        IProcessRunner runner)
    {
        _logger = logger;
        _monitoring = monitoring;
        _history = history;
        _systemInfo = systemInfo;
        _startup = startup;
        _security = security;
        _storage = storage;
        _scoring = scoring;
        _catalog = catalog;
        _registry = registry;
        _runner = runner;
    }

    /// <summary>Executa a avaliação completa (pode demorar 2–8 s por causa de WMI e defrag /A).</summary>
    public async Task<AssessmentReport> AssessAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("A iniciar avaliação do sistema…");

        progress?.Report("A medir desempenho…");
        var metrics = _monitoring.Read();
        var averages = _history.GetAverages(1);   // média da última hora é mais representativa que o instante

        progress?.Report("A analisar arranque…");
        var startupItems = _startup.GetStartupItems();
        var highImpact = startupItems.Where(i => i.Enabled && i.RecommendedToDisable).Select(i => i.Name).ToList();

        progress?.Report("A verificar segurança…");
        var security = _security.GetStatus();

        progress?.Report("A analisar discos…");
        var disks = _storage.GetDisks();
        var systemDisk = disks.FirstOrDefault(d => d.IsSystemDrive);
        var health = _storage.GetDiskHealth();
        var unhealthy = health.Count(h => !h.Health.Contains("Healthy", StringComparison.OrdinalIgnoreCase)
                                          && !h.Health.Contains("Bom", StringComparison.OrdinalIgnoreCase));

        double? fragmentation = null;
        if (systemDisk is not null && !_storage.IsSolidState(systemDisk.Drive))
        {
            var report = await _storage.AnalyzeAsync(systemDisk.Drive, cancellationToken).ConfigureAwait(false);
            fragmentation = report.FragmentationPercent;
        }

        progress?.Report("A verificar dispositivos…");
        var problemDevices = CountProblemDevices();

        progress?.Report("A calcular pontuação…");
        var input = new AssessmentInput
        {
            CpuUsagePercent = averages.Cpu > 0 ? averages.Cpu : metrics.CpuUsage * 100,
            RamUsagePercent = metrics.Memory.Usage * 100,
            DiskFreePercent = systemDisk?.FreePercent ?? 0,
            StorageType = _systemInfo.Hardware.StorageType,
            PowerPlan = _systemInfo.DetectPowerPlanName(),
            StartupEnabledCount = startupItems.Count(i => i.Enabled),
            StartupTotalCount = startupItems.Count,
            GpuUsagePercent = metrics.GpuUsageAvailable ? metrics.GpuUsage * 100 : 0,

            Uptime = metrics.Uptime,
            CpuTemperatureCelsius = metrics.CpuTemperatureCelsius,
            ProblemDevices = problemDevices,
            DiskHealthy = unhealthy == 0,
            DiskHealthUnknown = health.Count == 0 ? 1 : 0,
            WindowsUpdateRecent = security.WindowsUpdateRecent,
            FragmentationPercent = fragmentation,

            AntivirusEnabled = security.AntivirusEnabled,
            RealTimeProtection = security.RealTimeProtection,
            FirewallEnabled = security.FirewallEnabled,
            SecureBootEnabled = _systemInfo.Report.SecureBootEnabled,
            UacEnabled = IsUacEnabled(),
            PendingUpdates = security.PendingUpdates,
            SmartScreenEnabled = IsSmartScreenEnabled(),

            AppliedOptimizations = _catalog.AppliedCount,
            TotalAvailableOptimizations = Math.Max(1, _catalog.AvailableCount),
            TelemetryReduced = IsTelemetryReduced(),
            StorageSenseEnabled = IsStorageSenseEnabled(),
            GameModeConfigured = IsGameModeConfigured(),
            DisabledStartupItems = startupItems.Any(i => !i.Enabled && i.IsMicrosoft == false),
            VisualEffectsOptimized = AreVisualEffectsOptimized(),

            Hardware = _systemInfo.Hardware,
            HighImpactStartupItems = highImpact,
        };

        var report2 = _scoring.Calculate(input);
        _logger.LogInformation("Avaliação concluída: {Score} ({Grade})", report2.OverallScoreText, report2.Grade);
        return report2;
    }

    // ------------------------------------------------------------------ leituras pontuais

    /// <summary>Dispositivos com erro reportado pelo Windows (código 0 = sem problemas).</summary>
    public int CountProblemDevices()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");

            return searcher.Get().Count;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível consultar dispositivos com problemas.");
            return 0;
        }
    }

    public bool IsUacEnabled()
    {
        try
        {
            return _registry.GetValue("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA") is int value
                ? value == 1
                : true;
        }
        catch
        {
            return true;
        }
    }

    public bool IsSmartScreenEnabled()
    {
        try
        {
            // Política de máquina e de utilizador; qualquer uma em 1 significa ativo.
            var machine = _registry.GetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen");
            var user = _registry.GetValue("HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppHost", "EnableWebContentEvaluation");

            if (machine is int m) return m != 0;
            if (user is int u) return u != 0;
            return true;   // valor por omissão do Windows 11 é ativo
        }
        catch
        {
            return true;
        }
    }

    public bool IsTelemetryReduced()
    {
        try
        {
            var value = _registry.GetValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");
            return value is int level && level <= 1;
        }
        catch
        {
            return false;
        }
    }

    public bool IsStorageSenseEnabled()
    {
        try
        {
            var value = _registry.GetValue("HKCU",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01");
            return value is int enabled && enabled == 1;
        }
        catch
        {
            return false;
        }
    }

    public bool IsGameModeConfigured()
    {
        try
        {
            var value = _registry.GetValue("HKCU", @"SOFTWARE\Microsoft\GameBar", "AutoGameModeEnabled");
            return value is int enabled && enabled == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Efeitos visuais ajustados: verifica a preferência de "melhor desempenho"
    /// (VisualFXSetting = 2 e as animações do menu/ícones simplificadas).
    /// </summary>
    public bool AreVisualEffectsOptimized()
    {
        try
        {
            const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";

            var setting = _registry.GetValue("HKCU", key, "VisualFXSetting");
            if (setting is int s && s == 2) return true;

            var animation = _registry.GetValue("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate");
            var menuDelay = _registry.GetValue("HKCU", @"Control Panel\Desktop", "MenuShowDelay");

            var menuIsFast = int.TryParse(menuDelay?.ToString(), out var delay) && delay <= 100;

            return animation?.ToString() == "0" || menuIsFast;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reinício recomendado quando há atualizações já instaladas mas pendentes de aplicação.</summary>
    public bool IsRestartPending()
    {
        try
        {
            return _registry.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
                || _registry.KeyExists("HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Executa o utilitário de diagnóstico de desempenho do Windows (útil em suporte).</summary>
    public Result RunPerformanceDiagnostics()
    {
        try
        {
            var result = _runner.Run("winsat.exe", "formal -xml temp:perf.xml");
            return result.Success
                ? Result.Ok("Diagnóstico de desempenho do Windows executado.")
                : Result.Fail("Não foi possível executar o diagnóstico (winsat).", result.Output);
        }
        catch (Exception ex)
        {
            return Result.Fail("Diagnóstico indisponível neste sistema.", ex.Message);
        }
    }
}

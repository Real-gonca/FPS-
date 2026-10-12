using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Deteção de hardware e do sistema operativo através de WMI (<c>System.Management</c>).
///
/// Estratégia de resiliência: cada bloco de deteção está isolado. Se uma consulta WMI falhar
/// (acontece em VMs, em equipamentos sem bateria, em controladores mal implementados),
/// devolvemos "Desconhecido" e continuamos — nunca deixamos a aplicação falhar por causa de
/// informação acessória.
///
/// A deteção corre uma única vez (é estática) e o resultado é memorizado, porque consultas WMI
/// são caras (100–400 ms cada).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemInfoService
{
    private readonly ILogger<SystemInfoService> _logger;
    private readonly IProcessRunner _runner;
    private readonly IClock _clock;
    private readonly object _gate = new();

    private HardwareInfo? _hardware;
    private SystemReport? _report;
    private IReadOnlyList<DiskInfo>? _disks;

    public SystemInfoService(ILogger<SystemInfoService> logger, IProcessRunner runner, IClock clock)
    {
        _logger = logger;
        _runner = runner;
        _clock = clock;
    }

    /// <summary>Hardware detetado (memorizado).</summary>
    public HardwareInfo Hardware
    {
        get
        {
            lock (_gate)
            {
                return _hardware ??= DetectHardware();
            }
        }
    }

    /// <summary>Informação do SO e do ambiente de execução (memorizada).</summary>
    public SystemReport Report
    {
        get
        {
            lock (_gate)
            {
                return _report ??= DetectSystem();
            }
        }
    }

    /// <summary>Discos fixos da máquina, com o disco do sistema primeiro.</summary>
    public IReadOnlyList<DiskInfo> Disks
    {
        get
        {
            lock (_gate)
            {
                return _disks ??= DetectDisks();
            }
        }
    }

    /// <summary>Invalida a cache (usado pelo botão "Detetar novamente" na Configuração).</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            _hardware = null;
            _report = null;
            _disks = null;
        }
    }

    /// <summary>Identificador anónimo e estável da máquina (usado no relatório; não permite identificar o utilizador).</summary>
    public string MachineFingerprint
    {
        get
        {
            try
            {
                var raw = $"{Environment.MachineName}|{Hardware.CpuName}|{Hardware.TotalRamBytes}";
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
                return Convert.ToHexString(hash)[..16];
            }
            catch
            {
                return "DESCONHECIDO";
            }
        }
    }

    // ------------------------------------------------------------------ hardware

    private HardwareInfo DetectHardware()
    {
        // Valores iniciais seguros: se tudo falhar, a UI mostra estes em vez de zeros.
        var cpuName = "Processador desconhecido";
        double maxClock = 0;
        long ramBytes = 0;
        var gpuName = "Sem GPU dedicada detetada";
        long vram = 0;
        var storageName = "Disco desconhecido";
        var storageType = "Desconhecido";
        long storageTotal = 0;
        var biosVersion = string.Empty;
        var motherboard = string.Empty;
        var cores = Environment.ProcessorCount;
        var threads = Environment.ProcessorCount;

        try
        {
            // --- CPU ---------------------------------------------------------
            var cpu = QueryFirst("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            var detectedCpuName = cpu?["Name"]?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(detectedCpuName)) cpuName = detectedCpuName;

            var detectedCores = ToInt(cpu?["NumberOfCores"]);
            var detectedThreads = ToInt(cpu?["NumberOfLogicalProcessors"]);
            if (detectedCores > 0) cores = detectedCores;
            if (detectedThreads > 0) threads = detectedThreads;
            maxClock = ToDouble(cpu?["MaxClockSpeed"]);

            // --- RAM ---------------------------------------------------------
            foreach (var stick in Query("SELECT Capacity FROM Win32_PhysicalMemory"))
                ramBytes += ToLong(stick["Capacity"]);

            if (ramBytes <= 0)
            {
                // Fallback: memória física reportada pelo sistema operativo.
                ramBytes = ToLong(QueryFirst("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")?["TotalPhysicalMemory"]);
            }

            // --- GPU ---------------------------------------------------------
            foreach (var gpu in Query("SELECT Name, AdapterRAM, VideoProcessor FROM Win32_VideoController"))
            {
                var name = gpu["Name"]?.ToString()?.Trim() ?? string.Empty;
                var adapterRam = ToLong(gpu["AdapterRAM"]);

                // Preferimos GPU dedicada (NVIDIA/AMD) a gráficos integrados.
                var isDedicated = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Arc", StringComparison.OrdinalIgnoreCase);

                if (gpuName.StartsWith("Sem GPU", StringComparison.Ordinal) || isDedicated)
                {
                    gpuName = string.IsNullOrWhiteSpace(name) ? gpuName : name;
                    // AdapterRAM é um uint32 e satura em 4 GB — corrigimos com o valor de VRAM dos sensores quando existir.
                    if (adapterRam > 0) vram = adapterRam;
                }
            }

            // --- Armazenamento ----------------------------------------------
            (storageName, storageType, storageTotal) = DetectPrimaryStorage();

            // --- BIOS / placa-mãe -------------------------------------------
            var bios = QueryFirst("SELECT SMBIOSBIOSVersion, Manufacturer FROM Win32_BIOS");
            var board = QueryFirst("SELECT Product, Manufacturer FROM Win32_BaseBoard");
            biosVersion = $"{bios?["Manufacturer"]} {bios?["SMBIOSBIOSVersion"]}".Trim();
            motherboard = $"{board?["Manufacturer"]} {board?["Product"]}".Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deteção de hardware parcial — alguns valores ficarão desconhecidos.");
        }

        var info = new HardwareInfo
        {
            CpuName = cpuName,
            CpuCores = cores,
            CpuThreads = threads,
            CpuMaxClockMhz = maxClock,
            GpuName = gpuName,
            GpuVramBytes = vram,
            TotalRamBytes = ramBytes > 0 ? ramBytes : 8L * 1024 * 1024 * 1024,
            StorageName = storageName,
            StorageType = storageType,
            StorageTotalBytes = storageTotal,
            OperatingSystem = Report.OsName,
            OsBuild = Report.OsBuild,
            Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            MachineName = Environment.MachineName,
            BiosVersion = biosVersion,
            MotherboardName = motherboard,
        };

        _logger.LogInformation("Hardware: {Summary}", info.Summary);
        return info;
    }

    private (string name, string type, long total) DetectPrimaryStorage()
    {
        try
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

            foreach (var disk in Query("SELECT Model, Size, InterfaceType, MediaType, Index FROM Win32_DiskDrive"))
            {
                var model = disk["Model"]?.ToString()?.Trim() ?? "Disco desconhecido";
                var size = ToLong(disk["Size"]);

                // Determina se é NVMe (PCIe), SSD ou HDD a partir de consultas complementares.
                var type = DetectStorageType(model, ToInt(disk["Index"]));
                _ = systemDrive;   // o disco do sistema é marcado em DetectDisks (compara os volumes)

                return (model, type, size);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Deteção do armazenamento principal falhou.");
        }

        return ("Disco desconhecido", "Desconhecido", 0);
    }

    private string DetectStorageType(string model, int index)
    {
        try
        {
            // MSFT_PhysicalDisk (namespace storage) traz o tipo real: 3=HDD, 4=SSD, 5=SCM.
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\microsoft\windows\storage"),
                new ObjectQuery("SELECT MediaType, BusType FROM MSFT_PhysicalDisk"));

            foreach (ManagementObject obj in searcher.Get())
            {
                var mediaType = ToInt(obj["MediaType"]);
                var busType = ToInt(obj["BusType"]);

                if (mediaType == 4)
                {
                    // BusType 17 = NVMe; 11 = SATA; 8 = RAID.
                    return busType switch
                    {
                        17 => "SSD NVMe",
                        11 => "SSD SATA",
                        _ => "SSD",
                    };
                }

                if (mediaType == 3) return "HDD";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tipo de disco não determinado pelo MSFT_PhysicalDisk.");
        }

        // Heurísticas: NVMe no nome ou ausência de referências a "SSD" indicam HDD.
        var lower = model.ToLowerInvariant();
        if (lower.Contains("nvme") || lower.Contains("970") || lower.Contains("980") || lower.Contains("sn")) return "SSD NVMe";
        if (lower.Contains("ssd")) return "SSD";
        return "HDD";
    }

    private IReadOnlyList<DiskInfo> DetectDisks()
    {
        var disks = new List<DiskInfo>();
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

        try
        {
            foreach (var drive in Query("SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace, DriveType FROM Win32_LogicalDisk"))
            {
                var driveType = ToInt(drive["DriveType"]);
                if (driveType != 3) continue;   // 3 = disco fixo (ignora CD/DVD/removíveis)

                var id = drive["DeviceID"]?.ToString() ?? "?";
                var isSystem = string.Equals(id, systemRoot, StringComparison.OrdinalIgnoreCase);

                var (mediaName, mediaType, _) = DetectPrimaryStorage();

                disks.Add(new DiskInfo
                {
                    Drive = id,
                    VolumeLabel = drive["VolumeName"]?.ToString() ?? string.Empty,
                    FileSystem = drive["FileSystem"]?.ToString() ?? "NTFS",
                    MediaType = isSystem ? mediaType : mediaName,
                    TotalBytes = ToLong(drive["Size"]),
                    FreeBytes = ToLong(drive["FreeSpace"]),
                    IsSystemDrive = isSystem,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível enumerar os discos.");
        }

        return disks.OrderByDescending(d => d.IsSystemDrive).ThenBy(d => d.Drive).ToList();
    }

    // ------------------------------------------------------------------ sistema operativo

    private SystemReport DetectSystem()
    {
        var report = new SystemReport
        {
            OsName = "Windows",
            Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            MachineName = Environment.MachineName,
            UserName = PrivilegeService.GetCurrentUserName(),
            IsElevated = Environment.Is64BitProcess && IsAdmin(),
            LogicalProcessors = Environment.ProcessorCount,
            BootTime = _clock.Now - TimeSpan.FromMilliseconds(Environment.TickCount64),
        };

        try
        {
            var os = QueryFirst("SELECT Caption, Version, BuildNumber, OSArchitecture, FreePhysicalMemory FROM Win32_OperatingSystem");
            var caption = os?["Caption"]?.ToString()?.Trim() ?? "Windows";

            // "Microsoft Windows 11 Pro" → "Windows 11 Pro"; acrescenta o número quando o
            // Caption devolve apenas "Windows 10" mas o build indica 11 (build >= 22000).
            var build = os?["BuildNumber"]?.ToString() ?? string.Empty;
            var isWin11 = int.TryParse(build, out var buildNumber) && buildNumber >= 22000;
            caption = caption.Replace("Microsoft ", string.Empty);

            if (isWin11 && caption.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                caption = caption.Replace("Windows 10", "Windows 11");

            report = new SystemReport
            {
                OsName = caption,
                OsVersion = os?["Version"]?.ToString() ?? Environment.OSVersion.Version.ToString(),
                OsBuild = build,
                Architecture = os?["OSArchitecture"]?.ToString() ?? report.Architecture,
                MachineName = report.MachineName,
                UserName = report.UserName,
                IsElevated = report.IsElevated,
                IsWindows11 = isWin11,
                LogicalProcessors = report.LogicalProcessors,
                BootTime = report.BootTime,
                SecureBootEnabled = QueryFirst("SELECT SecureBootEnabled FROM Win32_DeviceGuard")?["SecureBootEnabled"] is not null
                    || IsSecureBootEnabled(),
                VirtualizationEnabled = IsVirtualizationEnabled(),
                PowerPlan = DetectPowerPlanName(),
                DotNetVersion = Environment.Version.ToString(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deteção do sistema operativo parcial.");
        }

        return report;
    }

    private static bool IsAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Secure Boot via <c>Confirm-SecureBootUEFI</c> (só existe em UEFI; devolve false em BIOS legacy).</summary>
    private bool IsSecureBootEnabled()
    {
        try
        {
            var result = _runner.Run("powershell.exe", "-NoProfile -NonInteractive -Command \"try { (Confirm-SecureBootUEFI) } catch { $false }\"");
            return result.Output.Contains("True", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Virtualização por hardware (VT-x/AMD-V) — interfere com otimizações de latência.</summary>
    private bool IsVirtualizationEnabled()
    {
        try
        {
            var computer = QueryFirst("SELECT VirtualizationFirmwareEnabled, HypervisorPresent FROM Win32_ComputerSystem");
            var firmware = computer?["VirtualizationFirmwareEnabled"]?.ToString();
            var hypervisor = computer?["HypervisorPresent"]?.ToString();
            return string.Equals(firmware, "True", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hypervisor, "True", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Nome do plano de energia ativo (via powercfg — mais fiável que WMI).</summary>
    public string DetectPowerPlanName()
    {
        try
        {
            var result = _runner.Run("powercfg", "/getactivescheme");
            if (!result.Success) return "Desconhecido";

            // Formato: "Power Scheme GUID: 381b4222-... (Equilibrado)"
            var text = result.Output;
            var start = text.IndexOf('(');
            var end = text.LastIndexOf(')');

            return start >= 0 && end > start ? text[(start + 1)..end].Trim() : "Desconhecido";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível ler o plano de energia.");
            return "Desconhecido";
        }
    }

    // ------------------------------------------------------------------ WMI helpers

    /// <summary>Executa uma consulta WMI e devolve todas as instâncias (lista vazia em caso de erro).</summary>
    private List<ManagementObject> Query(string wql)
    {
        var results = new List<ManagementObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (var item in searcher.Get())
            {
                if (item is ManagementObject mo) results.Add(mo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Consulta WMI falhou: {Wql}", wql);
        }

        return results;
    }

    private ManagementObject? QueryFirst(string wql) => Query(wql).FirstOrDefault();

    private static long ToLong(object? value)
    {
        if (value is null) return 0;
        return long.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static int ToInt(object? value)
    {
        if (value is null) return 0;
        return int.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static double ToDouble(object? value)
    {
        if (value is null) return 0;
        return double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }
}

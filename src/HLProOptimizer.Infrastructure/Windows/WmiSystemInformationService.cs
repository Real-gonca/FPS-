using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Windows;

/// <summary>
/// Inventário de hardware e SO via WMI (<c>System.Management</c>).
/// </summary>
/// <remarks>
/// <para>
/// O perfil do sistema é cacheado por <see cref="ProfileCacheDuration"/> porque as
/// consultas WMI custam centenas de milissegundos e o hardware não muda durante a
/// execução. As temperaturas NUNCA são cacheadas (leitura sob demanda).
/// </para>
/// <para>
/// Toda consulta é isolada por try/catch: um namespace WMI ausente (ex.:
/// <c>MSAcpi_ThermalZoneTemperature</c> em desktops, <c>root\Microsoft\Windows\Storage</c>
/// em Windows antigos) nunca derruba a coleta inteira — o serviço degrada para
/// valores parciais.
/// </para>
/// </remarks>
public sealed class WmiSystemInformationService : ISystemInformationService
{
    private static readonly TimeSpan ProfileCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly object ProfileLock = new();

    private readonly IRegistryService _registry;
    private readonly ILogger<WmiSystemInformationService> _logger;
    private SystemProfile? _cachedProfile;
    private DateTime _cachedAt = DateTime.MinValue;

    /// <summary>Cria o serviço de informações do sistema.</summary>
    /// <param name="registry">Registro (usado para checar firewall e UAC).</param>
    /// <param name="logger">Logger.</param>
    public WmiSystemInformationService(IRegistryService registry, ILogger<WmiSystemInformationService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAdministrator => ElevationHelper.IsProcessElevated();

    /// <inheritdoc />
    public async Task<SystemProfile> GetSystemProfileAsync(CancellationToken cancellationToken = default)
    {
        lock (ProfileLock)
        {
            if (_cachedProfile is not null && DateTime.Now - _cachedAt < ProfileCacheDuration)
            {
                return _cachedProfile;
            }
        }

        var profile = await Task.Run(() => BuildProfile(cancellationToken), cancellationToken).ConfigureAwait(false);

        lock (ProfileLock)
        {
            _cachedProfile = profile;
            _cachedAt = DateTime.Now;
        }

        return profile;
    }

    /// <inheritdoc />
    public Task<TemperatureInfo> GetTemperatureAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            double? cpu = null;
            var source = TemperatureInfo.Unavailable.Source;

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\WMI",
                    "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

                foreach (var managementObject in searcher.Get())
                {
                    using (managementObject)
                    {
                        var raw = Convert.ToDouble(managementObject["CurrentTemperature"], CultureInfo.InvariantCulture);

                        // WMI retorna décimos de Kelvin.
                        var celsius = (raw / 10d) - 273.15d;

                        if (celsius is > 0 and < 150)
                        {
                            cpu = cpu is null ? celsius : Math.Max(cpu.Value, celsius);
                            source = "ACPI (WMI)";
                        }
                    }
                }
            }
            catch (ManagementException ex)
            {
                _logger.LogDebug(ex, "Namespace de temperatura ACPI indisponível neste equipamento.");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Falha ao ler a temperatura via WMI.");
            }

            return cpu is null
                ? TemperatureInfo.Unavailable
                : new TemperatureInfo(cpu, null, source, DateTime.Now);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DriverInfo>> GetDriversAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var drivers = new List<DriverInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceName, Manufacturer, DriverVersion, DriverDate, DeviceClass, InfName, DeviceID, IsSigned " +
                    "FROM Win32_PnPSignedDriver");

                foreach (var managementObject in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (managementObject)
                    {
                        drivers.Add(new DriverInfo
                        {
                            DeviceName = GetString(managementObject, "DeviceName"),
                            Manufacturer = GetString(managementObject, "Manufacturer"),
                            Version = GetString(managementObject, "DriverVersion"),
                            DriverDate = ParseWmiDate(GetString(managementObject, "DriverDate")),
                            DeviceClass = GetString(managementObject, "DeviceClass"),
                            InfPath = GetString(managementObject, "InfName"),
                            DeviceId = GetString(managementObject, "DeviceID"),
                            IsSigned = GetBool(managementObject, "IsSigned"),
                            Status = "OK"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enumerar drivers via Win32_PnPSignedDriver.");
            }

            _logger.LogInformation("{Count} driver(s) enumerado(s) via WMI.", drivers.Count);

            return drivers
                .Where(d => !string.IsNullOrWhiteSpace(d.DeviceName))
                .GroupBy(d => d.DeviceId + "|" + d.Version, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(d => d.DeviceName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DriverInfo>> GetProblemDevicesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var devices = new List<DriverInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, Manufacturer, Status, DeviceID, ConfigManagerErrorCode " +
                    "FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");

                foreach (var managementObject in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (managementObject)
                    {
                        var code = GetUInt32(managementObject, "ConfigManagerErrorCode");

                        devices.Add(new DriverInfo
                        {
                            DeviceName = GetString(managementObject, "Name"),
                            Manufacturer = GetString(managementObject, "Manufacturer"),
                            DeviceId = GetString(managementObject, "DeviceID"),
                            Status = $"Code {code}",
                            Version = string.Empty
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enumerar dispositivos com problema.");
            }

            return devices;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> IsAntivirusActiveAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\SecurityCenter2",
                    "SELECT productName, productState FROM AntiVirusProduct");

                foreach (var managementObject in searcher.Get())
                {
                    using (managementObject)
                    {
                        var state = GetUInt32(managementObject, "productState");

                        // O byte intermediário de productState indica se a proteção está ligada.
                        var isOn = ((state >> 8) & 0xFF) switch
                        {
                            0x00 => false,
                            0x01 => true,
                            0x10 => false,
                            0x11 => true,
                            _ => true
                        };

                        if (isOn)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Não foi possível consultar o SecurityCenter2; usando o serviço WinDefend.");
            }

            // Fallback: serviço do Windows Defender em execução.
            try
            {
                using var service = new ServiceController("WinDefend");

                return service.Status == ServiceControllerStatus.Running;
            }
            catch (Exception)
            {
                return false;
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsFirewallActiveAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            const string policyRoot = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
            string[] profiles = ["DomainProfile", "StandardProfile", "PublicProfile"];

            foreach (var profile in profiles)
            {
                if (_registry.GetDword(RegistryHiveKind.LocalMachine, $@"{policyRoot}\{profile}", "EnableFirewall") == 1)
                {
                    return true;
                }
            }

            return false;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsUacEnabledAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => _registry.GetDword(
            RegistryHiveKind.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "EnableLUA") == 1, cancellationToken);
    }

    /// <summary>Constrói o perfil completo do sistema.</summary>
    private SystemProfile BuildProfile(CancellationToken cancellationToken)
    {
        var cpu = ReadCpu(cancellationToken);
        var memory = ReadMemory(cancellationToken);
        var gpus = ReadGpus(cancellationToken);
        var os = ReadOperatingSystem(cancellationToken);
        var partitions = ReadPartitions(cancellationToken);
        var adapters = ReadNetworkAdapters(cancellationToken);
        var motherboard = ReadMotherboard(cancellationToken);

        var profile = new SystemProfile(cpu, memory, gpus, os, partitions, adapters, motherboard, DateTime.Now);

        _logger.LogInformation(
            "Perfil coletado: {Cpu} · {Memory} · {GpuCount} GPU(s) · {DiskCount} volume(s) · {AdapterCount} adaptador(es).",
            cpu.Name,
            ByteFormat.Format(memory.TotalBytes),
            gpus.Count,
            partitions.Count,
            adapters.Count);

        return profile;
    }

    /// <summary>Lê as informações do processador (Win32_Processor).</summary>
    private CpuInfo ReadCpu(CancellationToken cancellationToken)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, " +
                "CurrentClockSpeed, Architecture, VirtualizationFirmwareEnabled, L2CacheSize, L3CacheSize " +
                "FROM Win32_Processor");

            foreach (var managementObject in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (managementObject)
                {
                    var cores = (int)GetUInt32(managementObject, "NumberOfCores");
                    var logical = (int)GetUInt32(managementObject, "NumberOfLogicalProcessors");

                    return new CpuInfo(
                        Name: NormalizeWhitespace(GetString(managementObject, "Name")),
                        Manufacturer: GetString(managementObject, "Manufacturer"),
                        PhysicalCores: cores > 0 ? cores : Environment.ProcessorCount,
                        LogicalProcessors: logical > 0 ? logical : Environment.ProcessorCount,
                        MaxClockMHz: GetDouble(managementObject, "MaxClockSpeed"),
                        CurrentClockMHz: GetDouble(managementObject, "CurrentClockSpeed"),
                        Architecture: GetUInt32(managementObject, "Architecture") switch
                        {
                            0 => "x86",
                            5 => "ARM",
                            6 => "Itanium",
                            9 => "x64",
                            12 => "ARM64",
                            _ => Environment.Is64BitOperatingSystem ? "x64" : "x86"
                        },
                        VirtualizationEnabled: GetBool(managementObject, "VirtualizationFirmwareEnabled"),
                        L2CacheSizeKb: GetUInt32(managementObject, "L2CacheSize"),
                        L3CacheSizeKb: GetUInt32(managementObject, "L3CacheSize"));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler Win32_Processor; usando fallback do ambiente.");
        }

        return new CpuInfo(
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? CpuInfo.Unknown.Name,
            CpuInfo.Unknown.Manufacturer,
            Environment.ProcessorCount,
            Environment.ProcessorCount,
            0,
            0,
            Environment.Is64BitOperatingSystem ? "x64" : "x86",
            false);
    }

    /// <summary>Lê memória física, commit charge e detalhes dos módulos.</summary>
    private MemoryInfo ReadMemory(CancellationToken cancellationToken)
    {
        long total = 0;
        long available = 0;

        try
        {
            using var osSearcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");

            foreach (var managementObject in osSearcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (managementObject)
                {
                    // O WMI reporta esses valores em KB.
                    total = (long)GetUInt64(managementObject, "TotalVisibleMemorySize") * 1024;
                    available = (long)GetUInt64(managementObject, "FreePhysicalMemory") * 1024;
                }

                break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler Win32_OperatingSystem para memória.");
        }

        var speed = 0;
        var modules = 0;

        try
        {
            using var moduleSearcher = new ManagementObjectSearcher("SELECT Speed FROM Win32_PhysicalMemory");

            foreach (var managementObject in moduleSearcher.Get())
            {
                using (managementObject)
                {
                    modules++;
                    speed = Math.Max(speed, (int)GetUInt32(managementObject, "Speed"));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao ler Win32_PhysicalMemory.");
        }

        long cache = 0;
        long commitCharge = 0;
        long commitLimit = 0;

        if (OperatingSystem.IsWindows())
        {
            var info = new NativeMethods.PERFORMANCE_INFORMATION
            {
                cb = Marshal.SizeOf<NativeMethods.PERFORMANCE_INFORMATION>()
            };

            if (NativeMethods.GetPerformanceInfo(out info, info.cb))
            {
                var pageSize = info.PageSize > 0 ? info.PageSize : 4096;

                cache = info.SystemCache * pageSize;
                commitCharge = info.CommitTotal * pageSize;
                commitLimit = info.CommitLimit * pageSize;

                // WMI pode falhar; o kernel é a fonte alternativa confiável.
                if (total <= 0)
                {
                    total = info.PhysicalTotal * pageSize;
                    available = info.PhysicalAvailable * pageSize;
                }
            }
        }

        return new MemoryInfo(
            TotalBytes: total,
            AvailableBytes: Math.Min(available, total),
            CacheBytes: cache,
            CommitChargeBytes: commitCharge,
            CommitLimitBytes: commitLimit,
            SpeedMHz: speed,
            Modules: modules);
    }

    /// <summary>Lê as placas de vídeo (Win32_VideoController).</summary>
    private IReadOnlyList<GpuInfo> ReadGpus(CancellationToken cancellationToken)
    {
        var gpus = new List<GpuInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, VideoProcessor, AdapterRAM, DriverVersion, DriverDate, CurrentRefreshRate, " +
                "CurrentHorizontalResolution, CurrentVerticalResolution, Status FROM Win32_VideoController");

            foreach (var managementObject in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (managementObject)
                {
                    var horizontal = GetUInt32(managementObject, "CurrentHorizontalResolution");
                    var vertical = GetUInt32(managementObject, "CurrentVerticalResolution");

                    // AdapterRAM é uint32 no WMI: acima de 4 GB o valor vem truncado.
                    var adapterRam = GetUInt32(managementObject, "AdapterRAM");

                    gpus.Add(new GpuInfo(
                        Name: GetString(managementObject, "Name"),
                        VideoProcessor: GetString(managementObject, "VideoProcessor"),
                        AdapterRamBytes: adapterRam,
                        DriverVersion: GetString(managementObject, "DriverVersion"),
                        DriverDate: ParseWmiDate(GetString(managementObject, "DriverDate")),
                        CurrentRefreshRateHz: (int)GetUInt32(managementObject, "CurrentRefreshRate"),
                        CurrentResolution: horizontal > 0 && vertical > 0
                            ? $"{horizontal}x{vertical}"
                            : string.Empty,
                        Status: GetString(managementObject, "Status"),
                        // Um adaptador com resolução ativa é o que está alimentando um monitor.
                        IsPrimary: horizontal > 0));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler Win32_VideoController.");
        }

        return gpus
            .OrderByDescending(g => g.IsPrimary)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Lê as informações do sistema operacional (Win32_OperatingSystem).</summary>
    private OperatingSystemInfo ReadOperatingSystem(CancellationToken cancellationToken)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime, " +
                "RegisteredUser, SerialNumber FROM Win32_OperatingSystem");

            foreach (var managementObject in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (managementObject)
                {
                    var lastBoot = ParseWmiDate(GetString(managementObject, "LastBootUpTime"));

                    return new OperatingSystemInfo(
                        Caption: GetString(managementObject, "Caption"),
                        Version: GetString(managementObject, "Version"),
                        BuildNumber: GetString(managementObject, "BuildNumber"),
                        Architecture: GetString(managementObject, "OSArchitecture"),
                        InstallDate: ParseWmiDate(GetString(managementObject, "InstallDate")),
                        // Environment.TickCount64 é imune a ajustes de relógio.
                        LastBootUpTime: lastBoot ?? DateTime.Now.Subtract(TimeSpan.FromMilliseconds(Environment.TickCount64)),
                        IsElevated: IsAdministrator,
                        RegisteredUser: GetString(managementObject, "RegisteredUser"),
                        SerialNumber: GetString(managementObject, "SerialNumber"));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler Win32_OperatingSystem.");
        }

        return new OperatingSystemInfo(
            $"Microsoft Windows {Environment.OSVersion.Version}",
            Environment.OSVersion.Version.ToString(),
            Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture),
            Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
            null,
            DateTime.Now.Subtract(TimeSpan.FromMilliseconds(Environment.TickCount64)),
            IsAdministrator);
    }

    /// <summary>Lê os volumes lógicos (Win32_LogicalDisk) e o tipo de mídia de cada disco.</summary>
    private IReadOnlyList<DiskPartition> ReadPartitions(CancellationToken cancellationToken)
    {
        var partitions = new List<DiskPartition>();
        var ssdIndices = ReadSolidStateDiskIndices();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace, DriveType FROM Win32_LogicalDisk");

            var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System))
                ?.TrimEnd('\\', ':') ?? "C";

            foreach (var managementObject in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (managementObject)
                {
                    var size = GetUInt64(managementObject, "Size");

                    // Volumes sem tamanho (drive de disquete/leitor vazio) são ignorados.
                    if (size == 0)
                    {
                        continue;
                    }

                    var deviceId = GetString(managementObject, "DeviceID");

                    partitions.Add(new DiskPartition(
                        DeviceId: deviceId,
                        VolumeName: GetString(managementObject, "VolumeName"),
                        FileSystem: GetString(managementObject, "FileSystem"),
                        SizeBytes: (long)size,
                        FreeBytes: (long)GetUInt64(managementObject, "FreeSpace"),
                        DriveType: GetUInt32(managementObject, "DriveType") switch
                        {
                            2 => "Removable",
                            3 => "Fixed",
                            4 => "Network",
                            5 => "Optical",
                            6 => "RAM",
                            _ => "Unknown"
                        },
                        IsBootVolume: string.Equals(deviceId.TrimEnd(':'), systemDrive, StringComparison.OrdinalIgnoreCase),
                        IsSolidState: ssdIndices.Contains(FindDiskIndex(deviceId))));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler Win32_LogicalDisk.");
        }

        return partitions
            .OrderByDescending(p => p.IsBootVolume)
            .ThenBy(p => p.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Índices dos discos físicos de estado sólido (MSFT_PhysicalDisk.MediaType 3=SSD, 4=SCM).
    /// </summary>
    private HashSet<int> ReadSolidStateDiskIndices()
    {
        var indices = new HashSet<int>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"root\Microsoft\Windows\Storage"),
                new ObjectQuery("SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk"));

            foreach (var managementObject in searcher.Get())
            {
                using (managementObject)
                {
                    var mediaType = GetUInt32(managementObject, "MediaType");
                    var deviceId = GetString(managementObject, "DeviceId");

                    if (mediaType is 3 or 4 && int.TryParse(deviceId, out var index))
                    {
                        indices.Add(index);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MSFT_PhysicalDisk indisponível; tipo de mídia não detectado.");
        }

        return indices;
    }

    /// <summary>
    /// Descobre o índice do disco físico que hospeda um volume lógico,
    /// seguindo as associações Win32_LogicalDisk → Win32_DiskPartition → Win32_DiskDrive.
    /// </summary>
    private int FindDiskIndex(string deviceId)
    {
        try
        {
            using var partitionSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{deviceId}'}} " +
                "WHERE AssocClass=Win32_LogicalDiskToPartition");

            foreach (var partition in partitionSearcher.Get())
            {
                using (partition)
                {
                    var partitionId = GetString(partition, "DeviceID");

                    if (string.IsNullOrEmpty(partitionId))
                    {
                        continue;
                    }

                    using var driveSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId}'}} " +
                        "WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                    foreach (var drive in driveSearcher.Get())
                    {
                        using (drive)
                        {
                            var index = GetString(drive, "Index");

                            if (int.TryParse(index, out var parsed))
                            {
                                return parsed;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível associar {DeviceId} a um disco físico.", deviceId);
        }

        return -1;
    }

    /// <summary>Lê os adaptadores de rede físicos com IP/DNS/gateway.</summary>
    private IReadOnlyList<NetworkAdapterInfo> ReadNetworkAdapters(CancellationToken cancellationToken)
    {
        var adapters = new List<NetworkAdapterInfo>();

        try
        {
            // 1) Configurações IP indexadas pela descrição do adaptador.
            var configs = new Dictionary<string, AdapterIpConfig>(StringComparer.OrdinalIgnoreCase);

            using (var configSearcher = new ManagementObjectSearcher(
                       "SELECT Description, IPAddress, DNSServerSearchOrder, DefaultIPGateway, DHCPEnabled " +
                       "FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True"))
            {
                foreach (var managementObject in configSearcher.Get())
                {
                    using (managementObject)
                    {
                        var description = GetString(managementObject, "Description");
                        var addresses = ReadStringArray(managementObject, "IPAddress");

                        configs[description] = new AdapterIpConfig(
                            addresses.FirstOrDefault(a => a.Contains('.')) ?? string.Empty,
                            addresses.FirstOrDefault(a => a.Contains(':')) ?? string.Empty,
                            ReadStringArray(managementObject, "DNSServerSearchOrder"),
                            ReadStringArray(managementObject, "DefaultIPGateway").FirstOrDefault() ?? string.Empty,
                            GetBool(managementObject, "DHCPEnabled"));
                    }
                }
            }

            // 2) Adaptadores físicos.
            using var adapterSearcher = new ManagementObjectSearcher(
                "SELECT Name, Description, MACAddress, Speed, NetConnectionID, NetConnectionStatus " +
                "FROM Win32_NetworkAdapter WHERE PhysicalAdapter = True");

            foreach (var managementObject in adapterSearcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (managementObject)
                {
                    var description = GetString(managementObject, "Description");
                    var connectionId = GetString(managementObject, "NetConnectionID");

                    configs.TryGetValue(description, out var config);

                    adapters.Add(new NetworkAdapterInfo(
                        Name: string.IsNullOrWhiteSpace(connectionId) ? description : connectionId,
                        Description: description,
                        MacAddress: GetString(managementObject, "MACAddress"),
                        // Speed vem em bits/s (0 quando desconectado).
                        LinkSpeedMbps: GetUInt64(managementObject, "Speed") / 1_000_000d,
                        Ipv4Address: config?.Ipv4 ?? string.Empty,
                        Ipv6Address: config?.Ipv6 ?? string.Empty,
                        DnsServers: config?.DnsServers ?? [],
                        Gateway: config?.Gateway ?? string.Empty,
                        IsPhysical: true,
                        // 2 = conectado (NET_CONNECTION_STATUS_CONNECTED).
                        IsOperational: GetUInt32(managementObject, "NetConnectionStatus") == 2,
                        IsDhcpEnabled: config?.DhcpEnabled ?? false));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler os adaptadores de rede.");
        }

        return adapters
            .OrderByDescending(a => a.IsOperational)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Lê fabricante/modelo da placa-mãe (Win32_BaseBoard).</summary>
    private string ReadMotherboard(CancellationToken cancellationToken)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");

            foreach (var managementObject in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (managementObject)
                {
                    var manufacturer = GetString(managementObject, "Manufacturer");
                    var product = GetString(managementObject, "Product");

                    return string.IsNullOrWhiteSpace(product)
                        ? manufacturer
                        : NormalizeWhitespace($"{manufacturer} {product}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao ler Win32_BaseBoard.");
        }

        return "Desconhecida";
    }

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string GetString(ManagementBaseObject managementObject, string property)
        => managementObject[property]?.ToString() ?? string.Empty;

    private static bool GetBool(ManagementBaseObject managementObject, string property)
        => managementObject[property] switch
        {
            bool value => value,
            string text => bool.TryParse(text, out var parsed) && parsed,
            _ => false
        };

    private static uint GetUInt32(ManagementBaseObject managementObject, string property)
        => managementObject[property] switch
        {
            null => 0u,
            uint value => value,
            ushort value => value,
            int value => (uint)Math.Max(0, value),
            string text when uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            var other => Convert.ToUInt32(other, CultureInfo.InvariantCulture)
        };

    private static ulong GetUInt64(ManagementBaseObject managementObject, string property)
    {
        var value = managementObject[property];

        if (value is null)
        {
            return 0;
        }

        if (value is ulong unsigned)
        {
            return unsigned;
        }

        return ulong.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static double GetDouble(ManagementBaseObject managementObject, string property)
        => managementObject[property] is { } value
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
            : 0d;

    private static IReadOnlyList<string> ReadStringArray(ManagementBaseObject managementObject, string property)
        => managementObject[property] switch
        {
            string[] values => values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList(),
            _ => []
        };

    /// <summary>Converte datas WMI (formato CIM_DATETIME) para <see cref="DateTime"/>.</summary>
    private static DateTime? ParseWmiDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(value);
        }
        catch (Exception)
        {
            // Data WMI malformada (ex.: "00000000000000.000000+000") é tratada como ausente.
            return null;
        }
    }

    /// <summary>Configuração IP de um adaptador (leitura intermediária).</summary>
    /// <param name="Ipv4">Endereço IPv4.</param>
    /// <param name="Ipv6">Endereço IPv6.</param>
    /// <param name="DnsServers">Servidores DNS configurados.</param>
    /// <param name="Gateway">Gateway padrão.</param>
    /// <param name="DhcpEnabled">Se o IP é obtido via DHCP.</param>
    private sealed record AdapterIpConfig(
        string Ipv4,
        string Ipv6,
        IReadOnlyList<string> DnsServers,
        string Gateway,
        bool DhcpEnabled);
}

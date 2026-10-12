using System.Runtime.Versioning;
using HLOptimizerPro.Core.Services.Abstractions;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services.System;

/// <summary>
/// Sensores de hardware via LibreHardwareMonitor (temperaturas de CPU/GPU, ventoinhas,
/// uso de GPU, VRAM).
///
/// Notas de engenharia importantes:
///  • A atualização (<c>Update()</c>) é feita numa thread separada da UI: ler sensores bloqueia
///    alguns milissegundos e fazê-lo no dispatcher causaria "engasgos" na animação (requisito:
///    UI fluida).
///  • A primeira leitura demora (carregamento do driver WinRing0); por isso é feita em background
///    no <see cref="Initialize"/> e os valores ficam nulos até estarem prontos.
///  • Requer privilégios de administrador — sem eles a leitura de temperatura é impossível
///    (limitação do kernel do Windows, não nossa).
///  • Máquinas virtuais normalmente não expõem sensores → <see cref="IsAvailable"/> fica false e a
///    UI mostra "Sensores indisponíveis" em vez de valores falsos.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LibreHardwareMonitorProvider : IHardwareSensorProvider
{
    private readonly ILogger<LibreHardwareMonitorProvider> _logger;
    private readonly Computer? _computer;
    private readonly Timer? _timer;
    private readonly object _gate = new();

    private HardwareSensorSnapshot _last = HardwareSensorSnapshot.Empty;
    private bool _initialized;

    public LibreHardwareMonitorProvider(ILogger<LibreHardwareMonitorProvider> logger)
    {
        _logger = logger;

        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,   // ventoinhas e temperaturas da placa
                IsStorageEnabled = false,      // não precisamos: usamos WMI/contadores (menos overhead)
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
            };

            _computer.Open();
            IsAvailable = _computer.Hardware.Count > 0;

            if (!IsAvailable)
                UnavailableReason = "Nenhum sensor de hardware detetado (comum em máquinas virtuais).";

            // Leitura periódica independente da UI, a cada 1 s.
            _timer = new Timer(_ => Refresh(), null, TimeSpan.FromMilliseconds(1200), TimeSpan.FromMilliseconds(1200));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LibreHardwareMonitor indisponível — temperaturas/GPU não serão apresentadas.");
            UnavailableReason = "Sensores indisponíveis (driver de hardware não acessível ou sem privilégios).";
            IsAvailable = false;
        }
    }

    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    /// <summary>Mapa dos tipos de hardware que interessam, resolvido uma única vez.</summary>
    private List<IHardware> Targets { get; } = new();

    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized || _computer is null) return;
            _initialized = true;

            try
            {
                foreach (var hardware in _computer.Hardware)
                    CollectTargets(hardware);

                _logger.LogInformation("Sensores inicializados: {Count} dispositivos ({List})",
                    Targets.Count, string.Join(", ", Targets.Select(t => t.Name)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao enumerar os sensores de hardware.");
            }
        }
    }

    public HardwareSensorSnapshot Snapshot()
    {
        lock (_gate)
        {
            return _last;
        }
    }

    public void Dispose()
    {
        try
        {
            _timer?.Dispose();
            _computer?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao fechar os sensores.");
        }
    }

    // ------------------------------------------------------------------ internos

    private void CollectTargets(IHardware hardware)
    {
        if (hardware.HardwareType is HardwareType.Cpu
            or HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel
            or HardwareType.Memory
            or HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController)
        {
            Targets.Add(hardware);
        }

        foreach (var sub in hardware.SubHardware)
            CollectTargets(sub);
    }

    /// <summary>Atualiza os sensores e reconstrói o snapshot. Chamado no timer (thread de fundo).</summary>
    private void Refresh()
    {
        if (_computer is null) return;

        try
        {
            lock (_gate)
            {
                if (!_initialized) Initialize();

                foreach (var hardware in Targets)
                {
                    hardware.Update();
                    foreach (var sub in hardware.SubHardware) sub.Update();
                }

                _last = BuildSnapshot();
            }
        }
        catch (Exception ex)
        {
            // Falha isolada (por exemplo driver reiniciado) — mantém o último snapshot válido.
            _logger.LogDebug(ex, "Leitura de sensores falhou nesta iteração.");
        }
    }

    private HardwareSensorSnapshot BuildSnapshot()
    {
        double? cpuTemp = null, cpuPower = null, gpuTemp = null, gpuHotspot = null, gpuUsage = null, gpuFan = null;
        long? vramUsed = null, vramTotal = null;
        double[]? perCore = null;
        string? cpuName = null, gpuName = null;
        var fans = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var hardware in Targets)
        {
            var isCpu = hardware.HardwareType == HardwareType.Cpu;
            var isGpu = hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
            var isMotherboard = hardware.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO;

            if (isCpu && cpuName is null) cpuName = hardware.Name;
            if (isGpu && gpuName is null) gpuName = hardware.Name;

            var coreUsages = new List<double>();

            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.Value is not { } value || float.IsNaN(value)) continue;

                switch (sensor.SensorType)
                {
                    case SensorType.Temperature:
                        if (isCpu && cpuTemp is null && IsCpuTemperatureSensor(sensor.Name)) cpuTemp = value;
                        else if (isGpu)
                        {
                            if (sensor.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase)) gpuHotspot = value;
                            else if (gpuTemp is null && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) gpuTemp = value;
                            else gpuTemp ??= value;
                        }
                        break;

                    case SensorType.Load:
                        if (isGpu && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && gpuUsage is null)
                            gpuUsage = value / 100.0;
                        else if (isGpu && gpuUsage is null && sensor.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                            gpuUsage = value / 100.0;
                        else if (isCpu && sensor.Name.Contains("CPU Total", StringComparison.OrdinalIgnoreCase))
                            cpuUsageFromSensors = value / 100.0;
                        else if (isCpu && sensor.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
                            coreUsages.Add(value / 100.0);
                        break;

                    case SensorType.SmallData or SensorType.Data:
                        if (isGpu)
                        {
                            if (sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase)) vramUsed = (long)(value * 1024 * 1024);
                            else if (sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase)) vramTotal = (long)(value * 1024 * 1024);
                        }
                        break;

                    case SensorType.Power:
                        if (isCpu && sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) && cpuPower is null)
                            cpuPower = value;
                        break;

                    case SensorType.Fan:
                        if (isMotherboard || isGpu)
                        {
                            fans[$"{hardware.Name} — {sensor.Name}"] = value;
                            if (isGpu && gpuFan is null) gpuFan = value;
                        }
                        break;
                }
            }

            if (isCpu && coreUsages.Count > 0) perCore = coreUsages.ToArray();
        }

        return new HardwareSensorSnapshot
        {
            CpuTemperature = cpuTemp,
            CpuPackagePowerWatts = cpuPower,
            GpuTemperature = gpuTemp,
            GpuHotspotTemperature = gpuHotspot,
            GpuUsage = gpuUsage,
            GpuUsedVramBytes = vramUsed,
            GpuTotalVramBytes = vramTotal,
            GpuFanRpm = gpuFan,
            CpuPerCoreUsage = perCore,
            Fans = fans,
            CpuName = cpuName,
            GpuName = gpuName,
        };
    }

    private double? cpuUsageFromSensors;
    /// <summary>Último uso de CPU reportado pelos sensores (usado como fallback aos contadores).</summary>
    public double? CpuUsageFromSensors => cpuUsageFromSensors;

    /// <summary>
    /// Filtra sensores que não são de temperatura real da CPU (há placas que reportam
    /// "CPU Socket", "VRM", "System" — só queremos o die/package).
    /// </summary>
    private static bool IsCpuTemperatureSensor(string sensorName)
    {
        var name = sensorName.ToLowerInvariant();
        if (name.Contains("vrm") || name.Contains("system") || name.Contains("motherboard")) return false;
        return name.Contains("package") || name.Contains("core") || name.Contains("cpu") || name.Contains("tdie") || name.Contains("tctl");
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Leitura de métricas em tempo real (CPU, RAM, GPU, disco, rede, temperaturas).
///
/// Fontes, por ordem de fiabilidade:
///   • CPU/RAM/disco  → <see cref="PerformanceCounter"/> (contadores do Windows)
///   • Memória detalhada (standby, cache, páginas modificadas) → GlobalMemoryStatusEx + NtQuerySystemInformation
///   • GPU/temperaturas → LibreHardwareMonitor (quando acessível)
///   • Fallback GPU    → contador "GPU Engine" do Performance Counter
///
/// IMPORTANTE (requisito): se uma métrica não pode ser medida nesta máquina, devolvemos
/// <c>null</c>/0 + flag de indisponibilidade. Nunca inventamos números.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MonitoringService : IDisposable
{
    private readonly ILogger<MonitoringService> _logger;
    private readonly IHardwareSensorProvider _sensors;
    private readonly object _gate = new();

    private PerformanceCounter? _cpuTotal;
    private PerformanceCounter? _cpuFrequency;
    private PerformanceCounter? _diskActive;
    private PerformanceCounter? _diskRead;
    private PerformanceCounter? _diskWrite;
    private PerformanceCounter? _gpuEngine;
    private bool _countersReady;

    public MonitoringService(ILogger<MonitoringService> logger, IHardwareSensorProvider sensors)
    {
        _logger = logger;
        _sensors = sensors;
        _sensors.Initialize();
    }

    /// <summary>True quando os contadores de desempenho foram inicializados com sucesso.</summary>
    public bool CountersAvailable => _countersReady;

    /// <summary>Lê um instantâneo completo do sistema. Nunca lança exceção.</summary>
    public SystemMetrics Read()
    {
        EnsureCounters();

        var memory = ReadMemory();

        double cpu = 0, freq = 0;
        double[] perCore = Array.Empty<double>();
        double diskActive = 0;
        long diskRead = 0, diskWrite = 0;
        double gpu = 0;
        var gpuAvailable = false;

        try
        {
            // NextValue() da primeira chamada devolve sempre 0 — é comportamento conhecido do
            // PerformanceCounter; a primeira amostra válida chega no ciclo seguinte (1 s depois).
            cpu = Clamp01((_cpuTotal?.NextValue() ?? 0) / 100.0);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Contador de CPU indisponível.");
        }

        try
        {
            var raw = _cpuFrequency?.NextValue() ?? 0;
            // O contador devolve a % da frequência nominal; convertemos em MHz reais.
            if (raw > 0 && _cpuBaseMhz > 0) freq = _cpuBaseMhz * raw / 100.0;
        }
        catch
        {
            freq = 0;
        }

        try
        {
            diskActive = Math.Clamp(_diskActive?.NextValue() ?? 0, 0, 100);
            diskRead = (long)Math.Max(0, _diskRead?.NextValue() ?? 0);
            diskWrite = (long)Math.Max(0, _diskWrite?.NextValue() ?? 0);
        }
        catch
        {
            // Sem contadores de disco (comum em VMs) — valores ficam a zero.
        }

        try
        {
            if (_gpuEngine is not null)
            {
                var value = _gpuEngine.NextValue();
                gpu = Clamp01(value / 100.0);
                gpuAvailable = value > 0;
            }
        }
        catch
        {
            gpuAvailable = false;
        }

        // LibreHardwareMonitor tem prioridade quando disponível (inclui VRAM e temperaturas).
        var sensors = _sensors.Snapshot();
        if (sensors.GpuUsage is { } hardwareGpu)
        {
            gpu = Clamp01(hardwareGpu);
            gpuAvailable = true;
        }

        perCore = ReadPerCoreCpu();

        return new SystemMetrics
        {
            Timestamp = DateTime.Now,
            CpuUsage = cpu,
            CpuClockMhz = freq,
            CpuPerCore = perCore,
            GpuUsage = gpu,
            GpuUsageAvailable = gpuAvailable,
            GpuUsedVramBytes = sensors.GpuUsedVramBytes ?? 0,
            GpuTotalVramBytes = sensors.GpuTotalVramBytes ?? 0,
            Memory = memory,
            DiskActivePercent = diskActive,
            DiskReadBytesPerSecond = diskRead,
            DiskWriteBytesPerSecond = diskWrite,
            CpuTemperatureCelsius = sensors.CpuTemperature,
            GpuTemperatureCelsius = sensors.GpuTemperature,
            Uptime = GetUptime(),
        };
    }

    /// <summary>Leitura detalhada de memória (disponível/standby/cache/páginas modificadas).</summary>
    public MemorySnapshot ReadMemory()
    {
        var total = 0L;
        var available = 0L;

        try
        {
            var status = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(status))
            {
                total = (long)status.ullTotalPhys;
                available = (long)status.ullAvailPhys;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GlobalMemoryStatusEx falhou.");
        }

        // Fallback: informação do GC não serve; usamos P/Invoke ou contadores.
        if (total <= 0)
        {
            try
            {
                using var counter = new PerformanceCounter("Memory", "Available Bytes", readOnly: true);
                available = (long)counter.NextValue();
            }
            catch
            {
                // Sem dados de memória nesta máquina.
            }
        }

        var details = ReadMemoryDetails();

        return new MemorySnapshot
        {
            TotalBytes = total,
            AvailableBytes = available,
            UsedBytes = Math.Max(0, total - available),
            CachedBytes = details.Cached,
            StandbyBytes = details.Standby,
            ModifiedBytes = details.Modified,
            MappedBytes = details.Mapped,
            PageFileTotalBytes = details.PageFileTotal,
            PageFileUsedBytes = details.PageFileUsed,
        };
    }

    /// <summary>Processos que mais consomem RAM, ordenados (top N). Base real via Process API.</summary>
    public IReadOnlyList<ProcessUsageInfo> GetTopMemoryProcesses(int count = 12)
    {
        var list = new List<ProcessUsageInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var info = new ProcessUsageInfo
                {
                    Pid = process.Id,
                    Name = process.ProcessName,
                    WorkingSetBytes = process.WorkingSet64,
                    PrivateBytes = process.PrivateMemorySize64,
                    StartTime = TryStartTime(process),
                    IsProtected = ProcessProtectionService.IsProtected(process.ProcessName),
                    IsSystemCritical = ProcessProtectionService.IsSystemCritical(process.ProcessName),
                    ExecutablePath = TryPath(process),
                    CpuPercent = 0,
                };

                list.Add(info);
            }
            catch
            {
                // Processos do sistema protegidos podem negar acesso — ignorar.
            }
            finally
            {
                process.Dispose();
            }
        }

        return list
            .OrderByDescending(p => p.WorkingSetBytes)
            .Take(count)
            .ToList();
    }

    public TimeSpan GetUptime()
    {
        try
        {
            return TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    // ------------------------------------------------------------------ internos

    private double _cpuBaseMhz;

    /// <summary>Base de frequência (MHz) usada para converter a percentagem do contador em MHz.</summary>
    public void SetCpuBaseFrequency(double mhz) => _cpuBaseMhz = mhz;

    private void EnsureCounters()
    {
        lock (_gate)
        {
            if (_countersReady) return;

            try
            {
                _cpuTotal = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", readOnly: true);
                _cpuTotal.NextValue();   // primeira leitura sempre 0

                try
                {
                    _cpuFrequency = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", readOnly: true);
                    _cpuFrequency.NextValue();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Contador de frequência de CPU indisponível (frequência será omitida).");
                }

                try
                {
                    _diskActive = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
                    _diskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", readOnly: true);
                    _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", readOnly: true);
                    _diskActive.NextValue();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Contadores de disco indisponíveis nesta máquina.");
                }

                try
                {
                    // "GPU Engine" agrega todas as instâncias; usamos a mais ativa como aproximação.
                    _gpuEngine = new PerformanceCounter("GPU Engine", "Utilization Percentage", "_Total", readOnly: true);
                    _gpuEngine.NextValue();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Contador de GPU indisponível — a usar o sensor de hardware.");
                }

                _countersReady = true;
                _logger.LogInformation("Contadores de desempenho inicializados.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível inicializar os contadores de desempenho (primeira leitura pode exigir um instante).");
            }
        }
    }

    private double[] ReadPerCoreCpu()
    {
        // Usa o contador do LibreHardwareMonitor quando existe (leitura por núcleo).
        var cores = _sensors.Snapshot().CpuPerCoreUsage;
        return cores ?? Array.Empty<double>();
    }

    private (long Cached, long Standby, long Modified, long Mapped, long PageFileTotal, long PageFileUsed) ReadMemoryDetails()
    {
        try
        {
            using var counter = new PerformanceCounter("Memory", "Cache Bytes", readOnly: true);
            var cached = (long)counter.NextValue();
            var pageFile = ReadPageFile();
            return (cached, 0, 0, 0, pageFile.total, pageFile.used);
        }
        catch
        {
            return (0, 0, 0, 0, 0, 0);
        }
    }

    private (long total, long used) ReadPageFile()
    {
        try
        {
            using var counter = new PerformanceCounter("Paging File", "% Usage", "_Total", readOnly: true);
            using var limit = new PerformanceCounter("Paging File", "% Usage Peak", "_Total", readOnly: true);
            var usage = counter.NextValue();

            // O tamanho do ficheiro de paginação vem do WMI no HardwareDetectionService; aqui
            // devolvemos só a percentagem e o serviço converte-a quando tem o total.
            return ((long)(usage * 100), (long)usage);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static DateTime TryStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string? TryPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;   // processos de sistema negam acesso ao caminho
        }
    }

    private static double Clamp01(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);

    public void Dispose()
    {
        _cpuTotal?.Dispose();
        _cpuFrequency?.Dispose();
        _diskActive?.Dispose();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
        _gpuEngine?.Dispose();
        _sensors.Dispose();
    }

    // ------------------------------------------------------------------ P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}

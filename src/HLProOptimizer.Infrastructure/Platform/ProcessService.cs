using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Platform;

/// <summary>
/// Enumeração e manipulação de processos via <see cref="Process"/> + P/Invoke
/// (EmptyWorkingSet para liberação de RAM).
/// </summary>
public sealed class ProcessService : IProcessService
{
    /// <summary>Processos que nunca devem ser finalizados.</summary>
    private static readonly IReadOnlySet<string> CriticalProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Idle", "Memory Compression", "smss", "csrss", "wininit",
        "winlogon", "services", "lsass", "svchost", "fontdrvhost", "dwm", "explorer",
        "sihost", "taskhostw", "ctfmon", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchHost", "RuntimeBroker", "ApplicationFrameHost", "SystemSettings",
        "TextInputHost", "audiodg", "SecurityHealthService", "SecurityHealthSystray",
        "MsMpEng", "NisSrv", "WmiPrvSE", "dllhost", "conhost", "WindowsTerminal",
        "HLProOptimizer", "spoolsv", "nvcontainer", "nvdisplay.container", "RtkAudUService64"
    };

    private static readonly ConcurrentDictionary<int, (long Ticks, DateTime Timestamp)> CpuSamples = new();

    private readonly ILogger<ProcessService> _logger;

    /// <summary>Cria o serviço de processos.</summary>
    /// <param name="logger">Logger.</param>
    public ProcessService(ILogger<ProcessService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProcessSnapshot>> GetProcessesAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ProcessSnapshot>>(() => Snapshot(Process.GetProcesses(), cancellationToken), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcessSnapshot>> GetTopByCpuAsync(int count, CancellationToken cancellationToken = default)
    {
        var processes = await GetProcessesAsync(cancellationToken).ConfigureAwait(false);

        return processes
            .OrderByDescending(p => p.CpuPercent)
            .Take(Math.Max(1, count))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcessSnapshot>> GetTopByMemoryAsync(int count, CancellationToken cancellationToken = default)
    {
        var processes = await GetProcessesAsync(cancellationToken).ConfigureAwait(false);

        return processes
            .OrderByDescending(p => p.MemoryBytes)
            .Take(Math.Max(1, count))
            .ToList();
    }

    /// <inheritdoc />
    public Task<bool> TerminateAsync(int processId, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(processId);

                if (IsCritical(process.ProcessName))
                {
                    _logger.LogWarning("Tentativa de encerrar o processo crítico '{Name}' bloqueada.", process.ProcessName);
                    return false;
                }

                process.Kill(entireProcessTree: false);
                process.WaitForExit(3000);

                _logger.LogInformation("Processo {Pid} ({Name}) encerrado.", processId, process.ProcessName);

                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao encerrar o processo {Pid}.", processId);
                return false;
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> TerminateBackgroundProcessesAsync(
        IEnumerable<string> keepProcessNames,
        CancellationToken cancellationToken = default)
    {
        var keep = new HashSet<string>(CriticalProcesses, StringComparer.OrdinalIgnoreCase);

        foreach (var name in keepProcessNames ?? [])
        {
            keep.Add(Path.GetFileNameWithoutExtension(name));
        }

        var terminated = new List<string>();
        var processes = Process.GetProcesses();

        foreach (var process in processes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (keep.Contains(process.ProcessName))
                {
                    continue;
                }

                if (IsCritical(process.ProcessName))
                {
                    continue;
                }

                // Só encerra processos sem janela visível (background real).
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    continue;
                }

                var path = SafeGetPath(process);

                // Preserva drivers, serviços de áudio/vídeo e o próprio Windows.
                if (path is not null && IsProtectedPath(path))
                {
                    continue;
                }

                process.Kill(entireProcessTree: false);

                if (process.WaitForExit(2000))
                {
                    terminated.Add(process.ProcessName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Não foi possível encerrar o processo {Name}.", process.ProcessName);
            }
            finally
            {
                process.Dispose();
            }
        }

        _logger.LogInformation("{Count} processo(s) em segundo plano encerrado(s).", terminated.Count);

        return await Task.FromResult<IReadOnlyList<string>>(terminated).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> SetPriorityAsync(int processId, ProcessPriorityKind priority, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.PriorityClass = MapPriority(priority);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao definir a prioridade do processo {Pid}.", processId);
                return false;
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> BoostAsync(string processName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return false;
            }

            var normalized = Path.GetFileNameWithoutExtension(processName);
            var boosted = false;

            foreach (var process in Process.GetProcessesByName(normalized))
            {
                try
                {
                    process.PriorityClass = ProcessPriorityClass.High;
                    boosted = true;

                    _logger.LogInformation("Prioridade do processo '{Name}' (PID {Pid}) elevada para High.", normalized, process.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao elevar a prioridade de '{Name}' (PID {Pid}).", normalized, process.Id);
                }
                finally
                {
                    process.Dispose();
                }
            }

            return boosted;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> CompactBackgroundMemoryAsync(CancellationToken cancellationToken = default)
    {
        var before = GetAvailableMemory();

        return await Task.Run(() =>
        {
            var compacted = 0;

            foreach (var process in Process.GetProcesses())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (IsCritical(process.ProcessName))
                    {
                        continue;
                    }

                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        continue;
                    }

                    if (CompactWorkingSet(process.Id))
                    {
                        compacted++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Falha ao compactar o working set do processo {Name}.", process.ProcessName);
                }
                finally
                {
                    process.Dispose();
                }
            }

            // Aguarda o gerenciador de memória reavaliar as páginas liberadas.
            Thread.Sleep(400);

            var after = GetAvailableMemory();
            var freed = Math.Max(0, after - before);

            _logger.LogInformation("Working set compactado em {Count} processo(s); {Bytes} bytes liberados.", compacted, freed);

            return freed;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string?> FindRunningAsync(IEnumerable<string> processNames, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var names = processNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(Path.GetFileNameWithoutExtension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in names)
            {
                try
                {
                    var processes = Process.GetProcessesByName(name);

                    if (processes.Length > 0)
                    {
                        foreach (var process in processes)
                        {
                            process.Dispose();
                        }

                        return name;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Falha ao procurar o processo '{Name}'.", name);
                }
            }

            return null;
        }, cancellationToken);
    }

    /// <summary>Constrói os snapshots calculando o uso de CPU por diferença de tempo.</summary>
    private IReadOnlyList<ProcessSnapshot> Snapshot(Process[] processes, CancellationToken cancellationToken)
    {
        var result = new List<ProcessSnapshot>(processes.Length);
        var now = DateTime.UtcNow;
        var logicalProcessors = Math.Max(1, Environment.ProcessorCount);

        foreach (var process in processes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var path = SafeGetPath(process);
                var cpuPercent = CalculateCpuPercent(process, now, logicalProcessors);

                result.Add(new ProcessSnapshot(
                    Id: process.Id,
                    Name: process.ProcessName,
                    ExecutablePath: path,
                    Publisher: SafeGetPublisher(path),
                    MemoryBytes: SafeGetWorkingSet(process),
                    CpuPercent: cpuPercent,
                    ThreadCount: SafeGetThreads(process),
                    HandleCount: SafeGetHandles(process),
                    StartedAt: SafeGetStartTime(process),
                    IsSystemProcess: path is not null && IsProtectedPath(path),
                    IsCritical: IsCritical(process.ProcessName)));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Falha ao ler o processo {Pid}.", process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    /// <summary>Calcula o uso de CPU (%) de um processo entre duas amostras.</summary>
    private static double CalculateCpuPercent(Process process, DateTime now, int logicalProcessors)
    {
        long ticks;

        try
        {
            ticks = process.TotalProcessorTime.Ticks;
        }
        catch (Exception)
        {
            return 0d;
        }

        if (!CpuSamples.TryGetValue(process.Id, out var previous))
        {
            CpuSamples[process.Id] = (ticks, now);
            return 0d;
        }

        var elapsed = (now - previous.Timestamp).TotalMilliseconds;

        if (elapsed <= 0)
        {
            return 0d;
        }

        var used = (ticks - previous.Ticks) / TimeSpan.TicksPerMillisecond;
        var percent = used / elapsed * 100d / logicalProcessors;

        CpuSamples[process.Id] = (ticks, now);

        return Math.Clamp(percent, 0d, 100d);
    }

    /// <summary>Reduz o working set de um processo via EmptyWorkingSet.</summary>
    private static bool CompactWorkingSet(int processId)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation | NativeMethods.ProcessSetQuota,
            false,
            processId);

        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NativeMethods.EmptyWorkingSet(handle);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>Memória física disponível no momento.</summary>
    private static long GetAvailableMemory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        var status = new NativeMethods.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
        };

        return NativeMethods.GlobalMemoryStatusEx(ref status) ? (long)status.ullAvailPhys : 0;
    }

    /// <summary>Verifica se o nome pertence à lista de processos críticos.</summary>
    private static bool IsCritical(string processName) => CriticalProcesses.Contains(processName);

    /// <summary>Verifica se o caminho pertence a uma pasta protegida do Windows.</summary>
    private static bool IsProtectedPath(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        return path.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Converte a prioridade do domínio para a prioridade do BCL.</summary>
    private static ProcessPriorityClass MapPriority(ProcessPriorityKind priority) => priority switch
    {
        ProcessPriorityKind.Idle => ProcessPriorityClass.Idle,
        ProcessPriorityKind.BelowNormal => ProcessPriorityClass.BelowNormal,
        ProcessPriorityKind.Normal => ProcessPriorityClass.Normal,
        ProcessPriorityKind.AboveNormal => ProcessPriorityClass.AboveNormal,
        ProcessPriorityKind.High => ProcessPriorityClass.High,
        ProcessPriorityKind.RealTime => ProcessPriorityClass.RealTime,
        _ => ProcessPriorityClass.Normal
    };

    private static string? SafeGetPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            // Acesso negado para processos de sistema/64-bits: esperado.
            return null;
        }
    }

    private static string SafeGetPublisher(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return "Desconhecido";
        }

        try
        {
            var company = FileVersionInfo.GetVersionInfo(path).CompanyName;
            return string.IsNullOrWhiteSpace(company) ? "Desconhecido" : company;
        }
        catch (Exception)
        {
            return "Desconhecido";
        }
    }

    private static long SafeGetWorkingSet(Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int SafeGetThreads(Process process)
    {
        try
        {
            return process.Threads.Count;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int SafeGetHandles(Process process)
    {
        try
        {
            return process.HandleCount;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static DateTime? SafeGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

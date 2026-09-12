using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Benchmark;

/// <summary>
/// Micro-benchmark local (spec §4.2) — mede o que a máquina faz AQUI AGORA:
///
///   CpuIndex         → FNV-1a sobre 1 MiB repetido até ~2 s de trabalho CPU puro;
///   MemAllocMbPerSec → alocação + escrita de blocos de 16 MiB até ~2 s;
///   DiskWriteMbPerSec→ escrita de 64 MiB num ficheiro temporário;
///   DiskReadMbPerSec → leitura do mesmo ficheiro.
///
/// Limites de tempo (time-boxed) para nunca competir com o sistema (spec §5).
/// Trabalho determinístico (mesmo padrão de bytes) — permite comparar
/// antes/depois na mesma máquina. Cada sub-medição falha para null (N/D).
/// </summary>
public sealed class LocalBenchmarkRunner : IBenchmarkRunner
{
    private static readonly int CpuWorkBytes = 1 << 20;          // 1 MiB
    private static readonly int MemBlockBytes = 16 << 20;        // 16 MiB
    private static readonly int DiskFileBytes = 64 << 20;        // 64 MiB
    private static readonly TimeSpan CpuBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MemBudget = TimeSpan.FromSeconds(2);

    private readonly ILogger<LocalBenchmarkRunner> _log;

    public LocalBenchmarkRunner(ILogger<LocalBenchmarkRunner> log) => _log = log;

    public async Task<BenchmarkResult> RunAsync(CancellationToken ct = default)
    {
        var total = Stopwatch.StartNew();
        double? cpu = null, mem = null, diskRead = null, diskWrite = null;

        try { cpu = await Task.Run(MeasureCpu, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { _log.LogDebug(ex, "Sub-medição CPU falhou → N/D."); }

        try { mem = await Task.Run(Allocate, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { _log.LogDebug(ex, "Sub-medição de memória falhou → N/D."); }

        var disk = await Task.Run(async () =>
        {
            await Task.Yield();
            double? w = null, r = null;
            try { w = WriteDisk(); }
            catch (Exception ex) { _log.LogDebug(ex, "Sub-medição de escrita em disco falhou → N/D."); }
            try { r = ReadDisk(); }
            catch (Exception ex) { _log.LogDebug(ex, "Sub-medição de leitura em disco falhou → N/D."); }
            return (w, r);
        }, ct);
        (diskWrite, diskRead) = disk;

        total.Stop();
        var result = new BenchmarkResult(
            DateTimeOffset.UtcNow, cpu, mem, diskRead, diskWrite, total.ElapsedMilliseconds);
        _log.LogInformation(
            "Micro-benchmark: CPU={Cpu} · Mem={Mem} MB/s · Disco E/W={W}/{R} MB/s · {Ms} ms",
            cpu?.ToString("0.00") ?? "N/D", mem?.ToString("0") ?? "N/D",
            diskWrite?.ToString("0") ?? "N/D", diskRead?.ToString("0") ?? "N/D",
            total.ElapsedMilliseconds);
        return result;
    }

    private static double MeasureCpu()
    {
        // FNV-1a 64 sobre um padrão fixo — trabalho CPU puro, determinístico.
        var data = new byte[CpuWorkBytes];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)((i * 31) & 0xFF);

        const ulong FnvOffset = 14695981039346656037UL;
        const ulong FnvPrime = 1099511628211UL;

        var sw = Stopwatch.StartNew();
        ulong hash = FnvOffset;
        long bytes = 0;
        do
        {
            for (int i = 0; i < data.Length; i++)
                hash = (hash ^ data[i]) * FnvPrime;
            bytes += data.Length;
        }
        while (sw.Elapsed < CpuBudget);

        sw.Stop();
        return hash switch
        {
            0 => 0, // caso de degenerescência teórica — evita dividir por hash 0
            _ => bytes / (sw.Elapsed.TotalSeconds * 1_000_000), // MiB/s processados
        };
    }

    private static double Allocate()
    {
        var block = new byte[MemBlockBytes];
        var sw = Stopwatch.StartNew();
        long total = 0;
        do
        {
            // Preencher (não só alocar) — mede alocação + primeira escrita real.
            for (int i = 0; i < block.Length; i += 64)
                block[i] = 0x5A;
            total += block.Length;
        }
        while (sw.Elapsed < MemBudget);

        sw.Stop();
        return total / (sw.Elapsed.TotalSeconds * (1024.0 * 1024.0));
    }

    private static string DiskFilePath() =>
        Path.Combine(Path.GetTempPath(), $"nexus-bench-{Guid.NewGuid():N}.bin");

    private static double WriteDisk()
    {
        string path = DiskFilePath();
        var buffer = new byte[DiskFileBytes];
        for (int i = 0; i < buffer.Length; i += 4096)
            buffer[i] = 0x42;

        var sw = Stopwatch.StartNew();
        using (var fs = File.Create(path))
        {
            fs.Write(buffer, 0, buffer.Length);
            fs.Flush(flushToDisk: true);
        }
        sw.Stop();

        try { File.Delete(path); } catch { /* melhor esforço */ }
        return buffer.Length / (sw.Elapsed.TotalSeconds * (1024.0 * 1024.0));
    }

    private static double ReadDisk()
    {
        string path = DiskFilePath();
        var buffer = new byte[DiskFileBytes];

        using (var fs = File.Create(path))
        {
            fs.Write(buffer, 0, buffer.Length);
            fs.Flush(flushToDisk: true);
        }

        var sw = Stopwatch.StartNew();
        var read = new byte[buffer.Length];
        using (var fs = File.OpenRead(path))
        {
            int remaining = read.Length;
            while (remaining > 0)
            {
                int n = fs.Read(read, read.Length - remaining, remaining);
                if (n <= 0)
                    break;
                remaining -= n;
            }
        }
        sw.Stop();

        try { File.Delete(path); } catch { /* melhor esforço */ }
        return read.Length / (sw.Elapsed.TotalSeconds * (1024.0 * 1024.0));
    }
}

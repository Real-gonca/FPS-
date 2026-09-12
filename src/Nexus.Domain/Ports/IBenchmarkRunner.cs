namespace Nexus.Domain.Ports;

/// <summary>
/// Resultado de um micro-benchmark A/B (spec §4.2: "benchmark medido
/// antes/depois"). Valores REAIS da máquina no instante da execução;
/// nulos = a sub-medição falhou (honestidade, nunca 0 fabricado).
///
/// TRANSPARENTIA (spec §0.2/§4.2): isto mede a velocidade do próprio
/// hardware/software (CPU, alocação, E/S de disco) — NÃO mede "performance
/// do sistema" de forma holística. A UI tem de etiquetar como micro-benchmark.
/// </summary>
public sealed record BenchmarkResult(
    DateTimeOffset TimestampUtc,
    double? CpuIndex,
    double? MemAllocMbPerSec,
    double? DiskReadMbPerSec,
    double? DiskWriteMbPerSec,
    double DurationMs);

public interface IBenchmarkRunner
{
    /// <summary>Executa o micro-benchmark (alguns segundos) e devolve a medição real.</summary>
    Task<BenchmarkResult> RunAsync(CancellationToken ct = default);
}

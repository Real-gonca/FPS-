using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Infrastructure.Benchmark;
using Xunit;

namespace Nexus.Tests.Benchmark;

/// <summary>
/// O micro-benchmark executa de facto (CPU ~2 s + memória ~2 s + disco 64 MiB)
/// e devolve valores REAIS — nunca zeros fabricados. (Duração total: ~5–8 s.)
/// </summary>
public class LocalBenchmarkRunnerTests
{
    private readonly LocalBenchmarkRunner _runner = new(NullLogger<LocalBenchmarkRunner>.Instance);

    [Fact]
    public async Task Run_ReturnsRealMeasurements()
    {
        var result = await _runner.RunAsync();

        Assert.True(result.DurationMs > 0);

        // Num ambiente normal as quatro sub-medições correm; se o ambiente
        // for exótico (sem TEMP, CPU restrita), apenas exige que pelo menos
        // uma sub-medição tenha valor real e que nenhuma "minta" (0 == N/D).
        int available = 0;
        if (result.CpuIndex is > 0) available++;
        if (result.MemAllocMbPerSec is > 0) available++;
        if (result.DiskReadMbPerSec is > 0) available++;
        if (result.DiskWriteMbPerSec is > 0) available++;

        Assert.True(available >= 1,
            "Nenhuma sub-medição produziu valor — o runner está a falhar em silêncio.");

        // Os valores devem ser plausíveis (não negativos, não absurdos).
        if (result.CpuIndex is { } cpu)
            Assert.InRange(cpu, 10, 1_000_000);
        if (result.MemAllocMbPerSec is { } mem)
            Assert.InRange(mem, 100, 100_000);
    }

    [Fact]
    public async Task Run_TimestampIsReal()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        var result = await _runner.RunAsync();
        var after = DateTimeOffset.UtcNow.AddMinutes(1);

        Assert.InRange(result.TimestampUtc, before, after);
    }
}

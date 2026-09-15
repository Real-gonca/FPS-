namespace HLProOptimizer.Core.Models;

/// <summary>
/// Amostra instantânea de desempenho coletada pelo monitor (1 amostra/segundo).
/// Alimenta os gráficos em tempo real do Dashboard e da tela de Monitoramento.
/// </summary>
/// <param name="Timestamp">Momento da coleta.</param>
/// <param name="CpuPercent">Uso de CPU [0-100].</param>
/// <param name="CpuTemperatureCelsius">Temperatura da CPU (null se indisponível).</param>
/// <param name="CpuClockGHz">Clock efetivo da CPU.</param>
/// <param name="MemoryPercent">Uso de memória [0-100].</param>
/// <param name="MemoryInUseBytes">Memória em uso.</param>
/// <param name="MemoryAvailableBytes">Memória disponível.</param>
/// <param name="GpuPercent">Uso da GPU [0-100].</param>
/// <param name="GpuTemperatureCelsius">Temperatura da GPU (null se indisponível).</param>
/// <param name="GpuMemoryUsedBytes">VRAM em uso.</param>
/// <param name="DiskReadBytesPerSecond">Leitura de disco.</param>
/// <param name="DiskWriteBytesPerSecond">Escrita de disco.</param>
/// <param name="DiskPercent">Uso de disco (tempo ativo) [0-100].</param>
/// <param name="NetworkDownloadBytesPerSecond">Download.</param>
/// <param name="NetworkUploadBytesPerSecond">Upload.</param>
/// <param name="NetworkLatencyMs">Latência em ms (null quando não medida).</param>
/// <param name="ProcessCount">Processos ativos.</param>
/// <param name="ThreadCount">Threads ativas.</param>
public sealed record PerformanceSample(
    DateTime Timestamp,
    double CpuPercent,
    double? CpuTemperatureCelsius,
    double CpuClockGHz,
    double MemoryPercent,
    long MemoryInUseBytes,
    long MemoryAvailableBytes,
    double GpuPercent,
    double? GpuTemperatureCelsius,
    long GpuMemoryUsedBytes,
    double DiskReadBytesPerSecond,
    double DiskWriteBytesPerSecond,
    double DiskPercent,
    double NetworkDownloadBytesPerSecond,
    double NetworkUploadBytesPerSecond,
    double? NetworkLatencyMs,
    int ProcessCount,
    int ThreadCount)
{
    /// <summary>Amostra zerada (estado inicial dos gráficos).</summary>
    public static PerformanceSample Empty { get; } = new(DateTime.Now, 0, null, 0, 0, 0, 0, 0, null, 0, 0, 0, 0, 0, 0, null, 0, 0);
}

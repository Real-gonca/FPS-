namespace HLOptimizerPro.Core.Services.Abstractions;

/// <summary>Leitura dos sensores de hardware (temperaturas, ventoinhas, GPU) num instante.</summary>
public sealed record HardwareSensorSnapshot
{
    public double? CpuTemperature { get; init; }
    public double? CpuPackagePowerWatts { get; init; }
    public double? GpuTemperature { get; init; }
    public double? GpuHotspotTemperature { get; init; }

    /// <summary>Uso da GPU em 0–1 (null quando o sensor não existe).</summary>
    public double? GpuUsage { get; init; }

    public long? GpuUsedVramBytes { get; init; }
    public long? GpuTotalVramBytes { get; init; }
    public double? GpuFanRpm { get; init; }

    /// <summary>Uso por núcleo lógico (0–1), quando o sensor expõe.</summary>
    public double[]? CpuPerCoreUsage { get; init; }

    /// <summary>Ventoinhas detetadas (nome → RPM).</summary>
    public IReadOnlyDictionary<string, double> Fans { get; init; } = new Dictionary<string, double>();

    /// <summary>Nome do dispositivo GPU usado (para o cartão de hardware).</summary>
    public string? GpuName { get; init; }
    public string? CpuName { get; init; }

    public static HardwareSensorSnapshot Empty { get; } = new();
}

/// <summary>
/// Abstração para os sensores de hardware. A implementação real usa LibreHardwareMonitor;
/// existe uma implementação nula para máquinas virtuais/CI onde o driver não existe.
/// </summary>
public interface IHardwareSensorProvider : IDisposable
{
    /// <summary>True quando há pelo menos um sensor utilizável.</summary>
    bool IsAvailable { get; }

    /// <summary>Motivo da indisponibilidade (mostrado na UI, para o utilizador perceber a ausência de temperatura).</summary>
    string? UnavailableReason { get; }

    /// <summary>Inicializa o subsistema de sensores (idempotente). Nunca lança exceção.</summary>
    void Initialize();

    /// <summary>Devolve a última leitura válida; em caso de erro devolve <see cref="HardwareSensorSnapshot.Empty"/>.</summary>
    HardwareSensorSnapshot Snapshot();
}

namespace Nexus.Domain.Ports;

/// <summary>
/// Resultado de um varrimento de tamanho de pasta com limite de tempo
/// (time-boxed, spec §2.3). <see cref="TimedOut"/> = true → a UI tem de
/// etiquetar o valor como "aprox.".
/// </summary>
public sealed record StorageProbeResult(double? TotalBytes, bool TimedOut, string RootPath);

public interface IStorageProbe
{
    Task<StorageProbeResult> MeasureAsync(string rootPath, TimeSpan timeBox, CancellationToken ct = default);
}

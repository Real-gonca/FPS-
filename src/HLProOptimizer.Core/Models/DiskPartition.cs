using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>Partição/volume lógico do sistema.</summary>
/// <param name="DeviceId">Letra da unidade (ex.: "C:").</param>
/// <param name="VolumeName">Rótulo do volume.</param>
/// <param name="FileSystem">NTFS, exFAT, ReFS...</param>
/// <param name="SizeBytes">Tamanho total.</param>
/// <param name="FreeBytes">Espaço livre.</param>
/// <param name="DriveType">Tipo (Fixed, Removable, Network, Optical...).</param>
/// <param name="IsBootVolume">Se contém o sistema operacional.</param>
/// <param name="IsSolidState">Se o disco físico subjacente é SSD/NVMe (quando detectável).</param>
public sealed record DiskPartition(
    string DeviceId,
    string VolumeName,
    string FileSystem,
    long SizeBytes,
    long FreeBytes,
    string DriveType,
    bool IsBootVolume,
    bool IsSolidState)
{
    /// <summary>Espaço usado em bytes.</summary>
    public long UsedBytes => Math.Max(0, SizeBytes - FreeBytes);

    /// <summary>Percentual de uso [0-100].</summary>
    public double UsedPercent => Percentage.Of(UsedBytes, SizeBytes);

    /// <summary>Percentual livre [0-100].</summary>
    public double FreePercent => 100d - UsedPercent;

    /// <summary>Saúde do volume baseada no espaço livre remanescente.</summary>
    public DiskHealth Health => FreePercent switch
    {
        < 5 => DiskHealth.Critical,
        < 10 => DiskHealth.Warning,
        _ => DiskHealth.Healthy
    };

    /// <summary>Texto de resumo (ex.: "C: · 512 GB · 42 GB livres").</summary>
    public string Summary => $"{DeviceId} · {ByteFormat.Format(SizeBytes)} · {ByteFormat.Format(FreeBytes)} livres";
}

using HLProOptimizer.Core.Common;

namespace HLProOptimizer.Core.Models;

/// <summary>Informações de memória física e virtual.</summary>
/// <param name="TotalBytes">Memória física total.</param>
/// <param name="AvailableBytes">Memória física disponível (free + standby).</param>
/// <param name="CacheBytes">Memória usada como cache de arquivos.</param>
/// <param name="ModifiedBytes">Páginas modificadas aguardando escrita.</param>
/// <param name="StandbyBytes">Páginas em standby (prontas para reuso).</param>
/// <param name="CommitChargeBytes">Commit charge atual.</param>
/// <param name="CommitLimitBytes">Limite de commit (RAM + pagefile).</param>
/// <param name="SpeedMHz">Velocidade do pente (0 quando indisponível).</param>
/// <param name="Modules">Quantidade de pentes detectados.</param>
public sealed record MemoryInfo(
    long TotalBytes,
    long AvailableBytes,
    long CacheBytes = 0,
    long ModifiedBytes = 0,
    long StandbyBytes = 0,
    long CommitChargeBytes = 0,
    long CommitLimitBytes = 0,
    int SpeedMHz = 0,
    int Modules = 0)
{
    /// <summary>Memória em uso (Total - Disponível).</summary>
    public long InUseBytes => Math.Max(0, TotalBytes - AvailableBytes);

    /// <summary>Percentual de uso [0-100].</summary>
    public double UsedPercent => Percentage.Of(InUseBytes, TotalBytes);

    /// <summary>Memória total formatada.</summary>
    public string TotalFormatted => ByteFormat.Format(TotalBytes);

    /// <summary>Memória disponível formatada.</summary>
    public string AvailableFormatted => ByteFormat.Format(AvailableBytes);

    /// <summary>Memória em uso formatada.</summary>
    public string InUseFormatted => ByteFormat.Format(InUseBytes);
}

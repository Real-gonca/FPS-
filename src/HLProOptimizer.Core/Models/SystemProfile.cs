namespace HLProOptimizer.Core.Models;

/// <summary>
/// Retrato completo do hardware/SO do computador. Produzido uma única vez no
/// startup (e sob demanda em "Atualizar") e consumido por todas as telas.
/// </summary>
/// <param name="Cpu">Processador.</param>
/// <param name="Memory">Memória.</param>
/// <param name="Gpus">Placas de vídeo (pode haver integradas + dedicadas).</param>
/// <param name="OperatingSystem">Sistema operacional.</param>
/// <param name="Partitions">Volumes lógicos.</param>
/// <param name="NetworkAdapters">Adaptadores de rede.</param>
/// <param name="Motherboard">Placa-mãe (fabricante/modelo).</param>
/// <param name="CollectedAt">Momento da coleta.</param>
public sealed record SystemProfile(
    CpuInfo Cpu,
    MemoryInfo Memory,
    IReadOnlyList<GpuInfo> Gpus,
    OperatingSystemInfo OperatingSystem,
    IReadOnlyList<DiskPartition> Partitions,
    IReadOnlyList<NetworkAdapterInfo> NetworkAdapters,
    string Motherboard,
    DateTime CollectedAt)
{
    /// <summary>GPU primária, ou a primeira disponível, ou <c>null</c>.</summary>
    public GpuInfo? PrimaryGpu =>
        Gpus.FirstOrDefault(g => g.IsPrimary) ?? Gpus.FirstOrDefault();

    /// <summary>Partição do sistema (C:).</summary>
    public DiskPartition? SystemPartition =>
        Partitions.FirstOrDefault(p => p.IsBootVolume) ?? Partitions.FirstOrDefault();

    /// <summary>Total de espaço livre em todos os volumes fixos.</summary>
    public long TotalFreeDiskBytes =>
        Partitions.Where(p => p.DriveType.Equals("Fixed", StringComparison.OrdinalIgnoreCase)).Sum(p => p.FreeBytes);

    /// <summary>Perfil vazio usado como fallback quando a coleta falha.</summary>
    public static SystemProfile Empty { get; } = new(
        CpuInfo.Unknown,
        new MemoryInfo(0, 0),
        [],
        new OperatingSystemInfo("Desconhecido", "0.0", "0", "x64", null, null, false),
        [],
        [],
        "Desconhecida",
        DateTime.Now);
}

namespace HLProOptimizer.Core.Models;

/// <summary>Informações estáticas do processador (obtidas via WMI Win32_Processor).</summary>
/// <param name="Name">Nome comercial (ex.: "Intel(R) Core(TM) i7-13700K").</param>
/// <param name="Manufacturer">Fabricante (Intel, AMD...).</param>
/// <param name="PhysicalCores">Número de núcleos físicos.</param>
/// <param name="LogicalProcessors">Número de threads lógicas.</param>
/// <param name="MaxClockMHz">Clock máximo de fábrica em MHz.</param>
/// <param name="CurrentClockMHz">Clock atual em MHz (0 quando indisponível).</param>
/// <param name="Architecture">Arquitetura (x64, ARM64...).</param>
/// <param name="VirtualizationEnabled">Se VT-x/AMD-V está habilitado.</param>
/// <param name="L2CacheSizeKb">Cache L2 em KB.</param>
/// <param name="L3CacheSizeKb">Cache L3 em KB.</param>
public sealed record CpuInfo(
    string Name,
    string Manufacturer,
    int PhysicalCores,
    int LogicalProcessors,
    double MaxClockMHz,
    double CurrentClockMHz,
    string Architecture,
    bool VirtualizationEnabled,
    long L2CacheSizeKb = 0,
    long L3CacheSizeKb = 0)
{
    /// <summary>Modelo com um <see cref="CpuInfo"/> vazio (fallback quando WMI falha).</summary>
    public static CpuInfo Unknown { get; } = new("Desconhecido", "Desconhecido", 0, 0, 0, 0, "x64", false);

    /// <summary>Clock máximo em GHz.</summary>
    public double MaxClockGHz => MaxClockMHz / 1000d;

    /// <summary>Clock atual em GHz.</summary>
    public double CurrentClockGHz => CurrentClockMHz / 1000d;

    /// <summary>Descrição resumida para exibição no Dashboard (ex.: "i7-13700K · 8C/16T").</summary>
    public string ShortDescription => $"{Name} · {PhysicalCores}C/{LogicalProcessors}T";
}

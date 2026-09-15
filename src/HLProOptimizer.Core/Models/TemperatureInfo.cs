namespace HLProOptimizer.Core.Models;

/// <summary>
/// Leitura de temperatura. Em muitos desktops o WMI (MSAcpi_ThermalZoneTemperature)
/// não expõe o sensor da CPU; nesses casos o valor é <c>null</c> e a UI exibe "N/D".
/// </summary>
/// <param name="CpuCelsius">Temperatura da CPU (null quando indisponível).</param>
/// <param name="GpuCelsius">Temperatura da GPU (null quando indisponível).</param>
/// <param name="Source">Origem da leitura (WMI, ACPI, LibreHardwareMemory...).</param>
/// <param name="Timestamp">Momento da leitura.</param>
public sealed record TemperatureInfo(
    double? CpuCelsius,
    double? GpuCelsius,
    string Source,
    DateTime Timestamp)
{
    /// <summary>Leitura vazia (sensores indisponíveis).</summary>
    public static TemperatureInfo Unavailable { get; } = new(null, null, "Indisponível", DateTime.Now);

    /// <summary>Indica se alguma temperatura foi obtida.</summary>
    public bool HasAnyReading => CpuCelsius.HasValue || GpuCelsius.HasValue;
}

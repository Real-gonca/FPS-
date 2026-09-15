namespace HLProOptimizer.Core.Models;

/// <summary>Driver de dispositivo instalado (Win32_PnPSignedDriver / pnputil).</summary>
public sealed class DriverInfo
{
    /// <summary>Nome do dispositivo.</summary>
    public required string DeviceName { get; init; }

    /// <summary>Fabricante.</summary>
    public string Manufacturer { get; init; } = "Desconhecido";

    /// <summary>Versão do driver.</summary>
    public string Version { get; init; } = "0.0";

    /// <summary>Data do driver.</summary>
    public DateTime? DriverDate { get; init; }

    /// <summary>Classe do dispositivo (Display, Net, USB...).</summary>
    public string DeviceClass { get; init; } = string.Empty;

    /// <summary>Device ID (ex.: "PCI\VEN_10DE&amp;DEV_2684").</summary>
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>Caminho do INF.</summary>
    public string InfPath { get; init; } = string.Empty;

    /// <summary>Nome do pacote publicado (oemXX.inf), quando houver.</summary>
    public string? PublishedName { get; init; }

    /// <summary>Se o driver é assinado digitalmente.</summary>
    public bool IsSigned { get; init; }

    /// <summary>Status do dispositivo (OK, Code 10, Code 43...).</summary>
    public string Status { get; init; } = "OK";

    /// <summary>True quando o dispositivo está com problema.</summary>
    public bool HasProblem =>
        !string.IsNullOrWhiteSpace(Status) &&
        !Status.Equals("OK", StringComparison.OrdinalIgnoreCase);
}

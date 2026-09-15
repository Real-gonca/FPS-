namespace HLProOptimizer.Core.Models;

/// <summary>Informações de uma placa de vídeo (Win32_VideoController).</summary>
/// <param name="Name">Nome do adaptador.</param>
/// <param name="VideoProcessor">Processador gráfico reportado.</param>
/// <param name="AdapterRamBytes">VRAM (limitada a 4 GB pelo WMI em adapters de 32 bits).</param>
/// <param name="DriverVersion">Versão do driver.</param>
/// <param name="DriverDate">Data do driver.</param>
/// <param name="CurrentRefreshRateHz">Taxa de atualização atual.</param>
/// <param name="CurrentResolution">Resolução atual (ex.: "2560x1440").</param>
/// <param name="Status">Status reportado pelo WMI (OK/Erro/Unknown).</param>
/// <param name="IsPrimary">Se é o adaptador primário.</param>
public sealed record GpuInfo(
    string Name,
    string VideoProcessor,
    long AdapterRamBytes,
    string DriverVersion,
    DateTime? DriverDate,
    int CurrentRefreshRateHz,
    string CurrentResolution,
    string Status,
    bool IsPrimary)
{
    /// <summary>Verdadeiro quando o driver parece ser o driver genérico da Microsoft.</summary>
    public bool IsGenericDriver =>
        Name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase);
}

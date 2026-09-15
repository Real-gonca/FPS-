namespace HLProOptimizer.Core.Models;

/// <summary>Informações do sistema operacional (Win32_OperatingSystem).</summary>
/// <param name="Caption">Nome amigável (ex.: "Microsoft Windows 11 Pro").</param>
/// <param name="Version">Versão (ex.: "10.0.22631").</param>
/// <param name="BuildNumber">Build do Windows.</param>
/// <param name="Architecture">Arquitetura (64-bit/ARM64).</param>
/// <param name="InstallDate">Data de instalação do SO.</param>
/// <param name="LastBootUpTime">Último boot (base do uptime).</param>
/// <param name="IsElevated">Se o processo atual tem token de administrador.</param>
/// <param name="RegisteredUser">Usuário registrado.</param>
/// <param name="SerialNumber">Serial da instalação.</param>
public sealed record OperatingSystemInfo(
    string Caption,
    string Version,
    string BuildNumber,
    string Architecture,
    DateTime? InstallDate,
    DateTime? LastBootUpTime,
    bool IsElevated,
    string RegisteredUser = "",
    string SerialNumber = "")
{
    /// <summary>Tempo ligado até <see cref="DateTime.Now"/>.</summary>
    public TimeSpan Uptime => LastBootUpTime is { } boot
        ? DateTime.Now - boot
        : TimeSpan.Zero;

    /// <summary>True para Windows 11 (build &gt;= 22000).</summary>
    public bool IsWindows11 =>
        int.TryParse(BuildNumber, out var build) && build >= 22000;

    /// <summary>True para Windows 10 (build 10240-19999).</summary>
    public bool IsWindows10 =>
        int.TryParse(BuildNumber, out var build) && build >= 10240 && build < 22000;

    /// <summary>Texto curto para o Dashboard (ex.: "Windows 11 Pro · 22631").</summary>
    public string ShortDescription => $"{Caption} · Build {BuildNumber}";
}

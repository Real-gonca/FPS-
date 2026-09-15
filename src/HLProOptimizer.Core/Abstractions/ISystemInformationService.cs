using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Inventário estático do hardware e do sistema operacional.
/// Implementado em Infrastructure com WMI (<c>System.Management</c>).
/// </summary>
public interface ISystemInformationService
{
    /// <summary>Indica se o processo atual possui token de administrador.</summary>
    bool IsAdministrator { get; }

    /// <summary>Coleta o perfil completo do sistema (CPU, RAM, GPU, SO, discos, rede).</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Perfil do sistema; nunca nulo (usa <see cref="SystemProfile.Empty"/> em falha parcial).</returns>
    Task<SystemProfile> GetSystemProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>Lê as temperaturas disponíveis (CPU/GPU).</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Leitura de temperatura ou <see cref="TemperatureInfo.Unavailable"/>.</returns>
    Task<TemperatureInfo> GetTemperatureAsync(CancellationToken cancellationToken = default);

    /// <summary>Lista drivers de dispositivos instalados.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<DriverInfo>> GetDriversAsync(CancellationToken cancellationToken = default);

    /// <summary>Lista drivers/dispositivos com problema.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<DriverInfo>> GetProblemDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Verifica se o antivírus (Windows Defender ou terceiro) está ativo.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> IsAntivirusActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Verifica se o firewall do Windows está ativo em todos os perfis.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> IsFirewallActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Verifica se o UAC está habilitado.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> IsUacEnabledAsync(CancellationToken cancellationToken = default);
}

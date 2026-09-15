using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Gerenciador de drivers (tela Ferramentas &gt; Drivers), baseado em pnputil/WMI.</summary>
public interface IDriverManagerService
{
    /// <summary>Lista os drivers de terceiros instalados no driver store.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<DriverInfo>> GetPublishedDriversAsync(CancellationToken cancellationToken = default);

    /// <summary>Lista dispositivos com problema (Code 10/28/43...).</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<DriverInfo>> GetProblemDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Força a atualização de um dispositivo via Windows Update.</summary>
    /// <param name="deviceId">Device ID.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CommandResult> UpdateDriverAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Reverte um driver para a versão anterior.</summary>
    /// <param name="deviceId">Device ID.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CommandResult> RollbackDriverAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Remove um pacote de driver do driver store.</summary>
    /// <param name="publishedName">Nome publicado (oemXX.inf).</param>
    /// <param name="deleteBinary">Se deve apagar também o binário.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CommandResult> UninstallDriverAsync(string publishedName, bool deleteBinary = false, CancellationToken cancellationToken = default);

    /// <summary>Procura por alterações de hardware.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CommandResult> ScanForHardwareChangesAsync(CancellationToken cancellationToken = default);
}

using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Backup e restauração (registro, hosts, configurações e pontos de restauração).</summary>
public interface IBackupService
{
    /// <summary>Faz backup de uma chave do registro em um arquivo .reg.</summary>
    /// <param name="keyPath">Caminho completo (ex.: "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run").</param>
    /// <param name="destinationFile">Arquivo .reg de destino (gerado automaticamente quando nulo).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Caminho do arquivo criado.</returns>
    Task<string> BackupRegistryAsync(string keyPath, string? destinationFile = null, CancellationToken cancellationToken = default);

    /// <summary>Restaura um arquivo .reg.</summary>
    /// <param name="regFile">Arquivo .reg.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> RestoreRegistryAsync(string regFile, CancellationToken cancellationToken = default);

    /// <summary>Cria um ponto de restauração do Windows.</summary>
    /// <param name="description">Descrição do ponto.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Informações do ponto criado, ou null em falha.</returns>
    Task<RestorePointInfo?> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default);

    /// <summary>Lista os pontos de restauração disponíveis.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<RestorePointInfo>> GetRestorePointsAsync(CancellationToken cancellationToken = default);

    /// <summary>Restaura o sistema a um ponto anterior.</summary>
    /// <param name="sequenceNumber">Número sequencial do ponto.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> RestoreToPointAsync(long sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>Exporta as configurações do aplicativo em JSON.</summary>
    /// <param name="destinationFile">Arquivo de destino.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<string> ExportSettingsAsync(string? destinationFile = null, CancellationToken cancellationToken = default);

    /// <summary>Importa configurações de um JSON exportado.</summary>
    /// <param name="sourceFile">Arquivo de origem.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<AppSettings?> ImportSettingsAsync(string sourceFile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cria um pacote de backup completo (registro Run + hosts + configurações +
    /// lista de inicialização) em uma pasta com timestamp.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Caminho da pasta de backup criada.</returns>
    Task<string> CreateFullBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>Lista os backups locais criados pelo aplicativo.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<string>> GetLocalBackupsAsync(CancellationToken cancellationToken = default);
}

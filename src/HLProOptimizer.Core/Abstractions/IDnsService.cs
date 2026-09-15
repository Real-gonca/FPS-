using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Configuração e diagnóstico de DNS.</summary>
public interface IDnsService
{
    /// <summary>Configurações de DNS por adaptador.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<DnsConfiguration>> GetConfigurationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Define DNS estático para um adaptador.</summary>
    /// <param name="adapterName">Nome da conexão.</param>
    /// <param name="primaryDns">Servidor primário.</param>
    /// <param name="secondaryDns">Servidor secundário.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> SetDnsAsync(string adapterName, string primaryDns, string? secondaryDns, CancellationToken cancellationToken = default);

    /// <summary>Aplica um preset de DNS a todos os adaptadores ativos.</summary>
    /// <param name="preset">Preset desejado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<int> ApplyPresetAsync(DnsPreset preset, CancellationToken cancellationToken = default);

    /// <summary>Volta o adaptador para DNS automático (DHCP).</summary>
    /// <param name="adapterName">Nome da conexão.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> ResetToAutomaticAsync(string adapterName, CancellationToken cancellationToken = default);

    /// <summary>Executa <c>ipconfig /flushdns</c>.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CommandResult> FlushCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>Lê o cache DNS atual (<c>ipconfig /displaydns</c>).</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<string>> GetCachedEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Habilita/desabilita DNS over HTTPS no Windows (netsh dns).</summary>
    /// <param name="adapterName">Nome da conexão.</param>
    /// <param name="enabled">Estado desejado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> SetDnsOverHttpsAsync(string adapterName, bool enabled, CancellationToken cancellationToken = default);
}

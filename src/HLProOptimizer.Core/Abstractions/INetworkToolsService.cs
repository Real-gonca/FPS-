using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Ferramentas de reparo de rede (tela Ferramentas &gt; Network Tools).</summary>
public interface INetworkToolsService
{
    /// <summary>Reinicia a pilha TCP/IP (<c>netsh int ip reset</c>).</summary>
    Task<CommandResult> ResetTcpIpAsync(CancellationToken cancellationToken = default);

    /// <summary>Reinicia o catálogo Winsock (<c>netsh winsock reset</c>).</summary>
    Task<CommandResult> ResetWinsockAsync(CancellationToken cancellationToken = default);

    /// <summary>Libera o endereço IP atual (<c>ipconfig /release</c>).</summary>
    Task<CommandResult> ReleaseIpAsync(CancellationToken cancellationToken = default);

    /// <summary>Renova o endereço IP (<c>ipconfig /renew</c>).</summary>
    Task<CommandResult> RenewIpAsync(CancellationToken cancellationToken = default);

    /// <summary>Reparo completo: winsock + TCP/IP + flush DNS + renew.</summary>
    /// <param name="progress">Log em tempo real.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<CommandResult>> RepairNetworkAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Mede a latência até um host (ping).</summary>
    /// <param name="host">Host ou IP.</param>
    /// <param name="timeoutMs">Timeout por tentativa.</param>
    /// <param name="attempts">Número de tentativas.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Latência média em ms, ou null se todas as tentativas falharem.</returns>
    Task<double?> MeasureLatencyAsync(string host, int timeoutMs = 1000, int attempts = 4, CancellationToken cancellationToken = default);

    /// <summary>Aplica ajustes de baixa latência (desativa Nagle/Energy-Efficient Ethernet, MTU).</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> ApplyLowLatencyTweaksAsync(CancellationToken cancellationToken = default);

    /// <summary>Reverte os ajustes de baixa latência.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> RevertLowLatencyTweaksAsync(CancellationToken cancellationToken = default);
}

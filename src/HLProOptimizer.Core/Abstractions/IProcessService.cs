using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Enumeração, priorização e finalização de processos.</summary>
public interface IProcessService
{
    /// <summary>Lista todos os processos acessíveis.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<ProcessSnapshot>> GetProcessesAsync(CancellationToken cancellationToken = default);

    /// <summary>Top N processos por uso de CPU.</summary>
    /// <param name="count">Quantidade de processos.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<ProcessSnapshot>> GetTopByCpuAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Top N processos por uso de memória.</summary>
    /// <param name="count">Quantidade de processos.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<ProcessSnapshot>> GetTopByMemoryAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Finaliza um processo pelo PID.</summary>
    /// <param name="processId">PID.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>True quando finalizado com sucesso.</returns>
    Task<bool> TerminateAsync(int processId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finaliza processos em segundo plano (Modo Gamer), preservando os nomes
    /// informados e os processos críticos do sistema.
    /// </summary>
    /// <param name="keepProcessNames">Nomes (sem .exe) que NÃO devem ser finalizados.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Nomes dos processos efetivamente finalizados.</returns>
    Task<IReadOnlyList<string>> TerminateBackgroundProcessesAsync(
        IEnumerable<string> keepProcessNames,
        CancellationToken cancellationToken = default);

    /// <summary>Define a classe de prioridade de um processo.</summary>
    /// <param name="processId">PID.</param>
    /// <param name="priority">Nova prioridade.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> SetPriorityAsync(int processId, ProcessPriorityKind priority, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reduz o working set dos processos em segundo plano, devolvendo RAM ao sistema.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Bytes liberados (estimados pela diferença de memória disponível).</returns>
    Task<long> CompactBackgroundMemoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Eleva a prioridade de CPU de todas as instâncias de um processo (usado pelo
    /// Modo Gamer para dar prioridade Alta ao jogo em execução).
    /// </summary>
    /// <param name="processName">Nome do processo (com ou sem .exe).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>True quando ao menos uma instância teve a prioridade alterada.</returns>
    Task<bool> BoostAsync(string processName, CancellationToken cancellationToken = default);

    /// <summary>Verifica se algum processo da lista está em execução (detecção de jogo).</summary>
    /// <param name="processNames">Nomes sem extensão.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Nome do primeiro processo encontrado, ou <c>null</c>.</returns>
    Task<string?> FindRunningAsync(IEnumerable<string> processNames, CancellationToken cancellationToken = default);
}

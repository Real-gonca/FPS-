using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Repositório do histórico de ações (EF Core + SQLite).</summary>
public interface IActionHistoryRepository
{
    /// <summary>Registra uma ação.</summary>
    /// <param name="record">Ação executada.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddAsync(ActionRecord record, CancellationToken cancellationToken = default);

    /// <summary>Registra várias ações em lote.</summary>
    /// <param name="records">Ações executadas.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddRangeAsync(IEnumerable<ActionRecord> records, CancellationToken cancellationToken = default);

    /// <summary>Últimas ações registradas.</summary>
    /// <param name="count">Quantidade máxima.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<ActionRecord>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default);

    /// <summary>Ações filtradas por tipo.</summary>
    /// <param name="kind">Tipo da ação.</param>
    /// <param name="count">Quantidade máxima.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<ActionRecord>> GetByKindAsync(ActionKind kind, int count = 50, CancellationToken cancellationToken = default);

    /// <summary>Total de ações registradas.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Apaga todo o histórico.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>Repositório de relatórios de análise.</summary>
public interface IScanReportRepository
{
    /// <summary>Persiste um relatório.</summary>
    /// <param name="report">Relatório gerado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task SaveAsync(ScanReport report, CancellationToken cancellationToken = default);

    /// <summary>Último relatório salvo.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<ScanReport?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>Relatório pelo identificador.</summary>
    /// <param name="id">GUID do relatório.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<ScanReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Histórico de relatórios.</summary>
    /// <param name="count">Quantidade máxima.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<ScanReport>> GetHistoryAsync(int count = 20, CancellationToken cancellationToken = default);
}

/// <summary>Repositório do estado persistente do Modo Gamer.</summary>
public interface IGameModeRepository
{
    /// <summary>Salva o estado (tweaks aplicados, plano de energia anterior).</summary>
    /// <param name="state">Estado atual.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task SaveStateAsync(GameModeState state, CancellationToken cancellationToken = default);

    /// <summary>Carrega o último estado salvo.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<GameModeState?> LoadStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Apaga o estado salvo.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

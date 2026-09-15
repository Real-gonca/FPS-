using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Fachada de registro de ações executadas pelo usuário. Alimenta a seção
/// "Ações recentes" do Dashboard e o relatório completo, garantindo que nenhuma
/// falha de persistência interrompa a operação principal.
/// </summary>
public interface IActionHistoryService
{
    /// <summary>Registra uma ação concluída (ou falha).</summary>
    /// <param name="kind">Tipo da ação.</param>
    /// <param name="title">Título curto.</param>
    /// <param name="details">Detalhes/resultados.</param>
    /// <param name="bytesAffected">Bytes afetados.</param>
    /// <param name="durationMs">Duração em milissegundos.</param>
    /// <param name="success">Se a ação foi bem-sucedida.</param>
    /// <param name="payloadJson">Dados adicionais serializados.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task RecordAsync(
        ActionKind kind,
        string title,
        string details = "",
        long bytesAffected = 0,
        long durationMs = 0,
        bool success = true,
        string? payloadJson = null,
        CancellationToken cancellationToken = default);

    /// <summary>Registra o resultado de uma otimização.</summary>
    /// <param name="result">Resultado consolidado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task RecordOptimizationAsync(OptimizationResult result, CancellationToken cancellationToken = default);

    /// <summary>Registra o resultado de uma limpeza.</summary>
    /// <param name="result">Resultado consolidado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task RecordCleanupAsync(CleanupResult result, CancellationToken cancellationToken = default);

    /// <summary>Registra o resultado de uma análise.</summary>
    /// <param name="report">Relatório gerado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task RecordAnalysisAsync(ScanReport report, CancellationToken cancellationToken = default);

    /// <summary>Ações mais recentes.</summary>
    /// <param name="count">Quantidade máxima.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<ActionRecord>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default);

    /// <summary>Apaga o histórico.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

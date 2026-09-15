using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Serviço de privacidade (telemetria, rastreamento, permissões, apps em background).</summary>
public interface IPrivacyService
{
    /// <summary>Lista todos os itens controláveis com seu estado atual.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<PrivacyItem>> GetItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>Itens agrupados por categoria.</summary>
    /// <param name="items">Itens a agrupar.</param>
    IReadOnlyDictionary<PrivacyCategory, IReadOnlyList<PrivacyItem>> GroupByCategory(IReadOnlyList<PrivacyItem> items);

    /// <summary>Aplica o estado desejado de um conjunto de itens.</summary>
    /// <param name="items">Itens com <c>IsProtected</c> já ajustado pela UI.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PrivacyApplyResult> ApplyAsync(IReadOnlyList<PrivacyItem> items, CancellationToken cancellationToken = default);

    /// <summary>Aplica apenas os itens marcados como recomendados.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PrivacyApplyResult> ApplyRecommendationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Restaura os valores padrão do Windows para todos os itens reversíveis.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PrivacyApplyResult> RestoreDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>Calcula o nível de proteção atual em percentual [0-100].</summary>
    /// <param name="items">Itens avaliados.</param>
    double CalculateProtectionScore(IReadOnlyList<PrivacyItem> items);
}

namespace HLProOptimizer.Core.Models;

/// <summary>Resultado da aplicação de itens de privacidade.</summary>
/// <param name="AppliedCount">Itens aplicados.</param>
/// <param name="FailedCount">Itens com falha.</param>
/// <param name="SkippedCount">Itens ignorados (exigiam admin e não foi concedido).</param>
/// <param name="RequiresElevation">Se algum item ficou pendente por falta de elevação.</param>
/// <param name="Errors">Mensagens de erro.</param>
public sealed record PrivacyApplyResult(
    int AppliedCount,
    int FailedCount,
    int SkippedCount,
    bool RequiresElevation,
    IReadOnlyList<string> Errors)
{
    /// <summary>Indica se todos os itens foram aplicados.</summary>
    public bool IsSuccess => FailedCount == 0 && SkippedCount == 0;

    /// <summary>Resumo textual para o log/histórico.</summary>
    public string Summary => $"{AppliedCount} aplicado(s), {SkippedCount} ignorado(s), {FailedCount} falha(s)";

    /// <summary>Resultado vazio.</summary>
    public static PrivacyApplyResult Empty { get; } = new(0, 0, 0, false, []);
}

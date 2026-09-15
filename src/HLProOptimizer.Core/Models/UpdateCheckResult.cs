namespace HLProOptimizer.Core.Models;

/// <summary>Resultado da verificação de atualizações.</summary>
/// <param name="IsAvailable">Se há nova versão.</param>
/// <param name="CurrentVersion">Versão instalada.</param>
/// <param name="LatestVersion">Versão disponível.</param>
/// <param name="ReleaseNotes">Notas da versão.</param>
/// <param name="DownloadUrl">URL de download.</param>
/// <param name="CheckedAt">Momento da verificação.</param>
/// <param name="Error">Erro de rede, quando houver.</param>
public sealed record UpdateCheckResult(
    bool IsAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string ReleaseNotes,
    string? DownloadUrl,
    DateTime CheckedAt,
    string? Error = null)
{
    /// <summary>Indica falha de verificação.</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Resultado "já está atualizado".</summary>
    public static UpdateCheckResult UpToDate(string currentVersion) =>
        new(false, currentVersion, currentVersion, string.Empty, null, DateTime.Now);

    /// <summary>Resultado de falha.</summary>
    public static UpdateCheckResult Failed(string currentVersion, string error) =>
        new(false, currentVersion, null, string.Empty, null, DateTime.Now, error);
}

using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Application.Cleanup;

/// <summary>
/// Mapeamento centralizado de <see cref="IssueCategory"/> para chaves de
/// localização e agrupamento de UI (evita strings mágicas espalhadas).
/// </summary>
public static class CategoryNames
{
    /// <summary>Chave de localização do nome de uma categoria.</summary>
    /// <param name="category">Categoria.</param>
    public static string LocalizationKey(IssueCategory category) => category switch
    {
        IssueCategory.TemporaryFiles => "Cat_TemporaryFiles",
        IssueCategory.SystemCache => "Cat_SystemCache",
        IssueCategory.LogsAndDumps => "Cat_LogsAndDumps",
        IssueCategory.Registry => "Cat_Registry",
        IssueCategory.StartupPrograms => "Cat_StartupPrograms",
        IssueCategory.Services => "Cat_Services",
        IssueCategory.Drivers => "Cat_Drivers",
        IssueCategory.WindowsUpdate => "Cat_WindowsUpdate",
        IssueCategory.RecycleBin => "Cat_RecycleBin",
        IssueCategory.BrowserCache => "Cat_BrowserCache",
        IssueCategory.Privacy => "Cat_Privacy",
        IssueCategory.Performance => "Cat_Performance",
        IssueCategory.Security => "Cat_Security",
        IssueCategory.Network => "Cat_Network",
        _ => category.ToString()
    };

    /// <summary>Chave de localização da severidade.</summary>
    /// <param name="severity">Severidade.</param>
    public static string SeverityKey(Severity severity) => severity switch
    {
        Severity.Informational => "Severity_Informational",
        Severity.Low => "Severity_Low",
        Severity.Medium => "Severity_Medium",
        Severity.High => "Severity_High",
        Severity.Critical => "Severity_Critical",
        _ => severity.ToString()
    };

    /// <summary>Ordem de exibição das categorias na tela de Limpeza.</summary>
    public static readonly IReadOnlyList<IssueCategory> CleanupOrder =
    [
        IssueCategory.TemporaryFiles,
        IssueCategory.SystemCache,
        IssueCategory.WindowsUpdate,
        IssueCategory.LogsAndDumps,
        IssueCategory.BrowserCache,
        IssueCategory.RecycleBin,
        IssueCategory.Registry
    ];
}

namespace HLProOptimizer.Core.Models;

/// <summary>Opções de diálogo de arquivo (independente de WPF).</summary>
/// <param name="Title">Título da janela.</param>
/// <param name="Filter">Filtro no formato "Descrição|*.ext|Todos|*.*".</param>
/// <param name="InitialDirectory">Pasta inicial.</param>
/// <param name="InitialFileName">Nome de arquivo sugerido.</param>
/// <param name="DefaultExtension">Extensão padrão.</param>
public sealed record FileDialogOptions(
    string Title,
    string Filter = "Todos os arquivos|*.*",
    string? InitialDirectory = null,
    string? InitialFileName = null,
    string? DefaultExtension = null);

namespace Nexus.Presentation.ViewModels;

/// <summary>
/// Placeholder HONESTO de módulos ainda por implementar: mostra o que o
/// módulo vai fazer e em que patch — sem nenhum dado fictício (spec §0.2).
/// </summary>
public sealed class PlaceholderViewModel
{
    public PlaceholderViewModel(NavItem item)
    {
        Glyph = item.Glyph;
        Title = item.Title;
        Description = item.Description;
        PatchRefText = string.IsNullOrWhiteSpace(item.PatchRef)
            ? "Disponível nesta versão"
            : "Planeado para o " + item.PatchRef + " — ver docs/patches/";
    }

    public string Glyph { get; }
    public string Title { get; }
    public string Description { get; }
    public string PatchRefText { get; }
}

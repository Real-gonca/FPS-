using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Catálogo das limpezas disponíveis.
///
/// Só entram aqui pastas cuja remoção é segura: ficheiros temporários do utilizador e do
/// sistema, caches reconstruíveis, pré-visualizações e relatórios. NUNCA entra aqui nada
/// dentro de <c>%USERPROFILE%\Documents</c>, <c>Downloads</c>, <c>Desktop</c> ou dados de
/// aplicações — apagar dados do utilizador não é "limpeza", é perda de dados.
/// </summary>
public static class CleanupTargets
{
    /// <summary>Todas as limpezas suportadas, com os caminhos reais resolvidos nesta máquina.</summary>
    public static IReadOnlyList<CleanupTarget> All()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

        var targets = new List<CleanupTarget>
        {
            new()
            {
                Key = "temp-user",
                Name = "Ficheiros temporários do utilizador",
                Description = "%TEMP% — ficheiros criados por instaladores e aplicações. Seguro de remover.",
                IconKind = "FolderRemove",
                Kind = CleanupKind.Files,
                Paths = new[] { Path.GetTempPath().TrimEnd('\\') },
            },
            new()
            {
                Key = "temp-windows",
                Name = "Ficheiros temporários do Windows",
                Description = @"C:\Windows\Temp — requer administrador; os ficheiros em uso são ignorados.",
                IconKind = "MicrosoftWindows",
                Kind = CleanupKind.Files,
                Paths = new[] { Path.Combine(windows, "Temp") },
            },
            new()
            {
                Key = "dns",
                Name = "Cache de DNS",
                Description = "Limpa os nomes de domínio memorizados (Bem: resolve problemas de acesso a sites).",
                IconKind = "Web",
                Kind = CleanupKind.DnsCache,
            },
            new()
            {
                Key = "thumbnails",
                Name = "Cache de miniaturas e ícones",
                Description = "O Explorer volta a gerar as pré-visualizações das imagens. Resolve ícones errados.",
                IconKind = "ImageMultiple",
                Kind = CleanupKind.ThumbnailCache,
                Paths = new[]
                {
                    Path.Combine(localAppData, @"Microsoft\Windows\Explorer"),
                },
            },
            new()
            {
                Key = "recycle-bin",
                Name = "Reciclagem",
                Description = "Esvazia a Reciclagem de todos os utilizadores. ATENÇÃO: os ficheiros não podem ser recuperados.",
                IconKind = "DeleteSweep",
                Kind = CleanupKind.RecycleBin,
                SelectedByDefault = false,
            },
            new()
            {
                Key = "windows-update",
                Name = "Cache do Windows Update",
                Description = @"C:\Windows\SoftwareDistribution\Download — ficheiros de atualizações já instaladas.",
                IconKind = "Update",
                Kind = CleanupKind.WindowsUpdateCache,
                Paths = new[] { Path.Combine(windows, @"SoftwareDistribution\Download") },
                SelectedByDefault = false,
            },
            new()
            {
                Key = "prefetch",
                Name = "Pré-carregamento (Prefetch)",
                Description = @"C:\Windows\Prefetch — acelera o arranque de programas; o Windows regenera automaticamente.",
                IconKind = "RocketLaunch",
                Kind = CleanupKind.Prefetch,
                Paths = new[] { Path.Combine(windows, "Prefetch") },
                SelectedByDefault = false,
            },
            new()
            {
                Key = "font-cache",
                Name = "Cache de tipos de letra",
                Description = "Resolve problemas de renderização de texto após instalar fontes.",
                IconKind = "FormatFont",
                Kind = CleanupKind.FontCache,
                Paths = new[] { Path.Combine(localAppData, "FontCache") },
                SelectedByDefault = false,
            },
            new()
            {
                Key = "reports",
                Name = "Relatórios de erro e diagnósticos",
                Description = "Relatórios de falhas guardados pelo Windows (ficheiros de diagnóstico antigos).",
                IconKind = "AlertCircleOutline",
                Kind = CleanupKind.Files,
                Paths = new[]
                {
                    Path.Combine(localAppData, @"Microsoft\Windows\WER\ReportArchive"),
                    Path.Combine(localAppData, @"Microsoft\Windows\WER\ReportQueue"),
                    Path.Combine(systemDrive + @"\", "ProgramData", @"Microsoft\Windows\WER\ReportArchive"),
                },
            },
            new()
            {
                Key = "hl-cache",
                Name = "Cache do HL Optimizer Pro",
                Description = "Relatórios antigos, backups de registo expirados e logs com mais de 30 dias.",
                IconKind = "Broom",
                Kind = CleanupKind.Files,
                Paths = new[] { Path.Combine(AppPaths.Root, "history") },
                SelectedByDefault = false,
            },
        };

        // Só devolvemos alvos cujos caminhos existem nesta máquina (ou que não dependem de caminhos).
        return targets
            .Where(t => t.Kind != CleanupKind.Files || t.Paths.Any(Directory.Exists))
            .ToList();
    }

    /// <summary>Alvos marcados por omissão para a "Limpeza Rápida" do Dashboard.</summary>
    public static IReadOnlyList<CleanupTarget> QuickTargets()
        => All().Where(t => t.SelectedByDefault).ToList();

    public static CleanupTarget? Find(string key)
        => All().FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
}

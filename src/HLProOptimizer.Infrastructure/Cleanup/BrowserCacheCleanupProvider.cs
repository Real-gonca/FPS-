using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Cleanup;

/// <summary>
/// Provedor de limpeza de cache dos navegadores e aplicativos Electron
/// (Chrome, Edge, Brave, Vivaldi, Opera, Firefox, Teams).
/// </summary>
/// <remarks>
/// <para>
/// <b>Só cache, nunca dados.</b> Histórico, cookies, senhas e favoritos ficam em
/// outros arquivos (<c>History</c>, <c>Cookies</c>, <c>Login Data</c>) que este
/// provedor não toca. Apagar cache custa apenas alguns segundos de recarga de sites.
/// </para>
/// <para>
/// <b>Perfis múltiplos.</b> Navegadores Chromium guardam um conjunto de pastas por
/// perfil (<c>Default</c>, <c>Profile 1</c>...). <see cref="FileCleanupProviderBase.ExpandProfiles"/>
/// descobre os perfis existentes em tempo de execução.
/// </para>
/// <para>Com o navegador aberto, os arquivos em uso são ignorados (contabilizados como "skipped").</para>
/// </remarks>
public sealed class BrowserCacheCleanupProvider : FileCleanupProviderBase
{
    /// <summary>Pastas de cache comuns a todos os navegadores Chromium.</summary>
    private static readonly string[] ChromiumCacheFolders =
    [
        // "Cache" já é recursivo: não listar subpastas dele (evita contagem dupla).
        "Cache",
        "Code Cache",
        "GPUCache",
        "DawnCache",
        "DawnGraphiteCache",
        "DawnWebGPUCache",
        @"Service Worker\CacheStorage",
        @"Service Worker\ScriptCache",
        @"Application Cache\Cache",
        @"File System\Cache"
    ];

    /// <summary>Cria o provedor de limpeza de cache de navegadores.</summary>
    /// <param name="fileSystem">Serviço de arquivos tolerante a falhas.</param>
    /// <param name="logger">Logger.</param>
    public BrowserCacheCleanupProvider(IFileSystemService fileSystem, ILogger<BrowserCacheCleanupProvider> logger)
        : base(fileSystem, logger)
    {
    }

    /// <inheritdoc />
    public override string Name => "Navegadores e Aplicativos";

    /// <inheritdoc />
    public override IssueCategory Category => IssueCategory.BrowserCache;

    /// <inheritdoc />
    public override int Order => 20;

    /// <inheritdoc />
    protected override IReadOnlyList<FileCleanupDefinition> BuildDefinitions()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return
        [
            new FileCleanupDefinition(
                Id: "browser.chrome",
                Name: "Cache do Google Chrome",
                Description: "Cache de páginas, scripts e GPU de todos os perfis do Chrome.",
                Category: IssueCategory.BrowserCache,
                PathResolver: () => ChromiumPaths(Path.Combine(localAppData, @"Google\Chrome\User Data")),
                Severity: Severity.Medium),

            new FileCleanupDefinition(
                Id: "browser.edge",
                Name: "Cache do Microsoft Edge",
                Description: "Cache de páginas, scripts e GPU de todos os perfis do Edge.",
                Category: IssueCategory.BrowserCache,
                PathResolver: () => ChromiumPaths(Path.Combine(localAppData, @"Microsoft\Edge\User Data")),
                Severity: Severity.Medium),

            new FileCleanupDefinition(
                Id: "browser.brave",
                Name: "Cache do Brave",
                Description: "Cache de páginas, scripts e GPU de todos os perfis do Brave.",
                Category: IssueCategory.BrowserCache,
                PathResolver: () => ChromiumPaths(Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data")),
                Severity: Severity.Low),

            new FileCleanupDefinition(
                Id: "browser.vivaldi",
                Name: "Cache do Vivaldi",
                Description: "Cache de páginas, scripts e GPU de todos os perfis do Vivaldi.",
                Category: IssueCategory.BrowserCache,
                PathResolver: () => ChromiumPaths(Path.Combine(localAppData, @"Vivaldi\User Data")),
                Severity: Severity.Low),

            new FileCleanupDefinition(
                Id: "browser.opera",
                Name: "Cache do Opera / Opera GX",
                Description: "Cache de páginas e GPU do Opera e do Opera GX.",
                Category: IssueCategory.BrowserCache,
                // O Opera não usa perfis: o cache fica direto na pasta da variante.
                PathResolver: () =>
                [
                    Path.Combine(appData, @"Opera Software\Opera Stable\Cache"),
                    Path.Combine(appData, @"Opera Software\Opera Stable\GPUCache"),
                    Path.Combine(appData, @"Opera Software\Opera GX Stable\Cache"),
                    Path.Combine(appData, @"Opera Software\Opera GX Stable\GPUCache")
                ],
                Severity: Severity.Low),

            new FileCleanupDefinition(
                Id: "browser.firefox",
                Name: "Cache do Mozilla Firefox",
                Description: "Cache de disco (cache2) de todos os perfis do Firefox.",
                Category: IssueCategory.BrowserCache,
                PathResolver: () =>
                [
                    .. ExpandProfiles(Path.Combine(localAppData, @"Mozilla\Firefox\Profiles"), "cache2"),
                    .. ExpandProfiles(Path.Combine(localAppData, @"Mozilla\Firefox\Profiles"), "shader-cache"),
                    .. ExpandProfiles(Path.Combine(localAppData, @"Mozilla\Firefox\Profiles"), "startupCache")
                ],
                Severity: Severity.Medium),

            new FileCleanupDefinition(
                Id: "app.teams",
                Name: "Cache do Microsoft Teams",
                Description: "Cache do Teams clássico (Electron). Não remove mensagens nem arquivos.",
                Category: IssueCategory.BrowserCache,
                PathResolver: () =>
                [
                    Path.Combine(appData, @"Microsoft\Teams\Cache"),
                    Path.Combine(appData, @"Microsoft\Teams\Code Cache"),
                    Path.Combine(appData, @"Microsoft\Teams\GPUCache"),
                    Path.Combine(appData, @"Microsoft\Teams\Service Worker\CacheStorage")
                ],
                Severity: Severity.Low,
                MinimumLevel: OptimizationLevel.Balanced),

            new FileCleanupDefinition(
                Id: "app.electron.misc",
                Name: "Cache de outros aplicativos (Electron)",
                Description: "Cache de GPU e de serviço de aplicativos como Discord, Spotify, Slack e VS Code.",
                Category: IssueCategory.BrowserCache,
                PathResolver: () =>
                [
                    Path.Combine(appData, @"discord\Cache"),
                    Path.Combine(appData, @"discord\Code Cache"),
                    Path.Combine(appData, @"discord\GPUCache"),
                    Path.Combine(localAppData, @"Spotify\Data"),
                    Path.Combine(localAppData, @"Spotify\Storage"),
                    Path.Combine(appData, @"Slack\Cache"),
                    Path.Combine(appData, @"Slack\Service Worker\CacheStorage"),
                    Path.Combine(appData, @"Code\Cache"),
                    Path.Combine(appData, @"Code\CachedData"),
                    Path.Combine(appData, @"Code\GPUCache")
                ],
                Severity: Severity.Low,
                MinimumLevel: OptimizationLevel.Balanced,
                SelectedByDefault: false)
        ];
    }

    /// <summary>Monta a lista de pastas de cache de um navegador Chromium (todos os perfis).</summary>
    private static string[] ChromiumPaths(string userDataPath)
    {
        var paths = new List<string>();

        foreach (var folder in ChromiumCacheFolders)
        {
            paths.AddRange(ExpandProfiles(userDataPath, folder));
        }

        // Alguns instaladores mantêm cache fora do perfil (ex.: "GrShaderCache").
        paths.Add(Path.Combine(userDataPath, "GrShaderCache"));
        paths.Add(Path.Combine(userDataPath, "ShaderCache"));

        return paths.ToArray();
    }
}

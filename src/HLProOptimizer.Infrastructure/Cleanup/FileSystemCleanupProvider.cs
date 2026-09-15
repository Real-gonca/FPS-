using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Cleanup;

/// <summary>
/// Provedor de limpeza de arquivos do sistema: temporários, caches, logs, dumps,
/// resíduos do Windows Update, Lixeira e instalações antigas.
/// </summary>
/// <remarks>
/// A lógica de scan/medição/exclusão vive em <see cref="FileCleanupProviderBase"/>;
/// aqui só existe o catálogo de alvos do Windows e a identidade do provedor.
/// </remarks>
public sealed class FileSystemCleanupProvider : FileCleanupProviderBase
{
    /// <summary>Cria o provedor de limpeza de arquivos do sistema.</summary>
    /// <param name="fileSystem">Serviço de arquivos tolerante a falhas.</param>
    /// <param name="logger">Logger.</param>
    public FileSystemCleanupProvider(IFileSystemService fileSystem, ILogger<FileSystemCleanupProvider> logger)
        : base(fileSystem, logger)
    {
    }

    /// <inheritdoc />
    public override string Name => "Sistema de Arquivos";

    /// <inheritdoc />
    public override IssueCategory Category => IssueCategory.TemporaryFiles;

    /// <inheritdoc />
    public override int Order => 10;

    /// <inheritdoc />
    /// <remarks>
    /// Parte dos alvos (C:\Windows\Temp, Windows Update, Prefetch) só pode ser limpa com
    /// administrador. Sem elevação o provedor continua funcionando e reporta esses alvos
    /// como ignorados.
    /// </remarks>
    public override bool RequiresAdmin => !Interop.ElevationHelper.IsProcessElevated();

    /// <inheritdoc />
    protected override IReadOnlyList<FileCleanupDefinition> BuildDefinitions()
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var systemDrive = Path.GetPathRoot(systemRoot)?.TrimEnd('\\') ?? "C:";

        return
        [
            new FileCleanupDefinition(
                Id: "temp.user",
                Name: "Temporários do usuário",
                Description: "Arquivos temporários criados pelos aplicativos do usuário atual.",
                Category: IssueCategory.TemporaryFiles,
                PathResolver: () => [Path.GetTempPath()],
                Severity: Severity.Medium,
                MinimumAge: TempMinimumAge),

            new FileCleanupDefinition(
                Id: "temp.windows",
                Name: "Temporários do Windows",
                Description: "Arquivos temporários do sistema (C:\\Windows\\Temp).",
                Category: IssueCategory.TemporaryFiles,
                PathResolver: () => [Path.Combine(systemRoot, "Temp")],
                Severity: Severity.Medium,
                RequiresAdmin: true,
                MinimumAge: TempMinimumAge),

            new FileCleanupDefinition(
                Id: "wu.download",
                Name: "Cache do Windows Update",
                Description: "Pacotes de atualização já instalados (SoftwareDistribution\\Download).",
                Category: IssueCategory.WindowsUpdate,
                PathResolver: () => [Path.Combine(systemRoot, @"SoftwareDistribution\Download")],
                Severity: Severity.High,
                RequiresAdmin: true,
                MinimumLevel: OptimizationLevel.Balanced),

            new FileCleanupDefinition(
                Id: "wu.delivery",
                Name: "Cache de Otimização de Entrega",
                Description: "Arquivos compartilhados com outros PCs pela Otimização de Entrega.",
                Category: IssueCategory.WindowsUpdate,
                PathResolver: () =>
                [
                    Path.Combine(systemRoot, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
                    Path.Combine(systemRoot, @"SoftwareDistribution\DeliveryOptimization")
                ],
                Severity: Severity.Medium,
                RequiresAdmin: true,
                MinimumLevel: OptimizationLevel.Balanced),

            new FileCleanupDefinition(
                Id: "logs.cbs",
                Name: "Logs do CBS (Component Based Servicing)",
                Description: "Logs de instalação de componentes e atualizações.",
                Category: IssueCategory.LogsAndDumps,
                PathResolver: () => [Path.Combine(systemRoot, @"Logs\CBS")],
                SearchPatterns: ["*.log", "*.cab", "*.tmp"],
                Severity: Severity.Low,
                RequiresAdmin: true,
                MinimumLevel: OptimizationLevel.Balanced),

            new FileCleanupDefinition(
                Id: "logs.wer",
                Name: "Relatórios de erro do Windows",
                Description: "Relatórios enviados (ou não) ao Windows Error Reporting.",
                Category: IssueCategory.LogsAndDumps,
                PathResolver: () =>
                [
                    Path.Combine(programData, @"Microsoft\Windows\WER\ReportQueue"),
                    Path.Combine(programData, @"Microsoft\Windows\WER\ReportArchive"),
                    Path.Combine(localAppData, @"Microsoft\Windows\WER")
                ],
                Severity: Severity.Medium,
                MinimumLevel: OptimizationLevel.Balanced),

            new FileCleanupDefinition(
                Id: "dumps.crash",
                Name: "Despejos de memória de aplicativos",
                Description: "Arquivos .dmp gerados quando um aplicativo trava.",
                Category: IssueCategory.LogsAndDumps,
                PathResolver: () =>
                [
                    Path.Combine(localAppData, "CrashDumps"),
                    Path.Combine(programData, @"Microsoft\Windows\WER\Minidump")
                ],
                SearchPatterns: ["*.dmp", "*.hdmp", "*.mdmp"],
                Severity: Severity.Low),

            new FileCleanupDefinition(
                Id: "dumps.memory",
                Name: "Despejo de memória do kernel",
                Description: "MEMORY.DMP e minidumps de tela azul (podem ocupar GBs).",
                Category: IssueCategory.LogsAndDumps,
                PathResolver: () => [Path.Combine(systemRoot, "Minidump"), systemRoot],
                SearchPatterns: ["MEMORY.DMP", "*.dmp"],
                Recursive: false,
                Severity: Severity.High,
                RequiresAdmin: true,
                MinimumLevel: OptimizationLevel.Balanced,
                SelectedByDefault: false),

            new FileCleanupDefinition(
                Id: "cache.thumbnails",
                Name: "Cache de miniaturas",
                Description: "Miniaturas de imagens e vídeos (serão regeneradas).",
                Category: IssueCategory.SystemCache,
                PathResolver: () => [Path.Combine(localAppData, @"Microsoft\Windows\Explorer")],
                SearchPatterns: ["thumbcache_*.db", "iconcache_*.db"],
                Recursive: false,
                Severity: Severity.Low),

            new FileCleanupDefinition(
                Id: "cache.shaders",
                Name: "Cache de shaders",
                Description: "Shaders compilados (DirectX, NVIDIA, AMD, Intel). Serão recompilados no primeiro uso.",
                Category: IssueCategory.SystemCache,
                PathResolver: () =>
                [
                    Path.Combine(localAppData, "D3DSCache"),
                    Path.Combine(localAppData, @"NVIDIA\DXCache"),
                    Path.Combine(localAppData, @"NVIDIA\GLCache"),
                    Path.Combine(localAppData, @"NVIDIA Corporation\NV_Cache"),
                    Path.Combine(localAppData, @"AMD\DxCache"),
                    Path.Combine(localAppData, @"AMD\GLCache"),
                    Path.Combine(localAppData, @"Intel\ShaderCache"),
                    Path.Combine(programData, @"NVIDIA Corporation\NV_Cache")
                ],
                Severity: Severity.Medium,
                MinimumLevel: OptimizationLevel.Balanced,
                SelectedByDefault: false),

            new FileCleanupDefinition(
                Id: "cache.internet",
                Name: "Cache de internet do sistema",
                Description: "Arquivos temporários de internet (WebView2, componentes do Windows).",
                Category: IssueCategory.SystemCache,
                PathResolver: () =>
                [
                    Path.Combine(localAppData, @"Microsoft\Windows\INetCache"),
                    Path.Combine(localAppData, @"Microsoft\Windows\WebCache"),
                    Path.Combine(localAppData, @"Microsoft\Windows\Explorer", @"IECompatCache")
                ],
                Severity: Severity.Low),

            new FileCleanupDefinition(
                Id: "cache.font",
                Name: "Cache de fontes",
                Description: "Cache do serviço de fontes do Windows.",
                Category: IssueCategory.SystemCache,
                PathResolver: () => [Path.Combine(systemRoot, @"ServiceProfiles\LocalService\AppData\Local\FontCache")],
                Severity: Severity.Low,
                RequiresAdmin: true,
                MinimumLevel: OptimizationLevel.Aggressive,
                SelectedByDefault: false),

            new FileCleanupDefinition(
                Id: "recyclebin",
                Name: "Lixeira",
                Description: "Todos os arquivos enviados para a Lixeira, em todas as unidades.",
                Category: IssueCategory.RecycleBin,
                PathResolver: () => [],
                Severity: Severity.Medium,
                IsRecycleBin: true),

            new FileCleanupDefinition(
                Id: "prefetch",
                Name: "Prefetch",
                Description: "Dados de pré-carregamento. O boot fica mais lento por alguns dias até o Windows reconstruir.",
                Category: IssueCategory.SystemCache,
                PathResolver: () => [Path.Combine(systemRoot, "Prefetch")],
                SearchPatterns: ["*.pf", "*.db"],
                Severity: Severity.Low,
                RequiresAdmin: true,
                IsSafe: false,
                MinimumLevel: OptimizationLevel.Aggressive,
                SelectedByDefault: false),

            new FileCleanupDefinition(
                Id: "windows.old",
                Name: "Instalação anterior do Windows (Windows.old)",
                Description: "Cópia da instalação anterior. Remover impede reverter a atualização.",
                Category: IssueCategory.WindowsUpdate,
                PathResolver: () => [Path.Combine(systemDrive, "Windows.old")],
                Severity: Severity.High,
                RequiresAdmin: true,
                IsSafe: false,
                MinimumLevel: OptimizationLevel.Aggressive,
                SelectedByDefault: false)
        ];
    }
}

using System.Reflection;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Infrastructure.Backup;
using HLProOptimizer.Infrastructure.Cleanup;
using HLProOptimizer.Infrastructure.Drivers;
using HLProOptimizer.Infrastructure.Hosts;
using HLProOptimizer.Infrastructure.Logging;
using HLProOptimizer.Infrastructure.Monitoring;
using HLProOptimizer.Infrastructure.Network;
using HLProOptimizer.Infrastructure.Persistence;
using HLProOptimizer.Infrastructure.Platform;
using HLProOptimizer.Infrastructure.Power;
using HLProOptimizer.Infrastructure.Repair;
using HLProOptimizer.Infrastructure.Update;
using HLProOptimizer.Infrastructure.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace HLProOptimizer.Infrastructure.DependencyInjection;

/// <summary>
/// Composição da camada Infrastructure (implementações Windows + persistência).
/// </summary>
/// <remarks>
/// <para>
/// <b>Tudo singleton</b> de propósito: os serviços mantêm estado caro de recriar
/// (contadores de desempenho do monitor, cache do perfil WMI, catálogo de caminhos)
/// e são consumidos por uma única janela do aplicativo. Exceção: os
/// <see cref="HlOptimizerDbContext"/>, criados sob demanda por
/// <see cref="IDbContextFactory{TContext}"/> porque DbContext não é thread-safe.
/// </para>
/// <para>
/// <b>Ordem importa</b>: caminhos (<see cref="ISystemPaths"/>) e logging são
/// registrados primeiro, pois o bootstrap do Serilog precisa da pasta de logs
/// antes de qualquer outro serviço existir.
/// </para>
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registra toda a camada Infrastructure (Windows, persistência, logging).</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="verboseLogging">Se o log deve incluir nível Debug (diagnóstico).</param>
    /// <returns>A mesma coleção, para encadeamento (fluent API).</returns>
    public static IServiceCollection AddHlOptimizerInfrastructure(this IServiceCollection services, bool verboseLogging = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCorePlatform();
        services.AddWindowsServices();
        services.AddCleanupProviders();
        services.AddPersistence();
        services.AddUpdateServices();
        services.AddLoggingPipeline(verboseLogging);

        return services;
    }

    /// <summary>
    /// Inicializa recursos que dependem de E/S (banco SQLite).
    /// Chamar uma vez no startup, depois de construir o <see cref="IServiceProvider"/>.
    /// </summary>
    /// <param name="provider">Provedor de serviços.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task InitializeHlOptimizerInfrastructureAsync(
        this IServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var factory = provider.GetRequiredService<IDbContextFactory<HlOptimizerDbContext>>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("HLProOptimizer.Bootstrap");

        await using var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await context.InitializeAsync(logger, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Banco de dados local pronto em {Path}.", provider.GetRequiredService<ISystemPaths>().DatabasePath);
    }

    /// <summary>Plataforma: caminhos, comandos, registro, arquivos, processos, serviços, elevação.</summary>
    private static void AddCorePlatform(this IServiceCollection services)
    {
        // Caminhos primeiro: o Serilog e o banco dependem deles.
        var paths = new SystemPaths();
        paths.EnsureCreated();

        services.AddSingleton<ISystemPaths>(paths);

        services.AddSingleton<ICommandRunner, CommandRunner>();
        services.AddSingleton<IRegistryService, WindowsRegistryService>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IServiceManager, WindowsServiceManager>();
        services.AddSingleton<IFileMetadataService, FileMetadataService>();
        services.AddSingleton<IElevationService, ElevationService>();
        services.AddSingleton<IScheduledTaskService, ScheduledTaskService>();

        // Executor compartilhado pelas ferramentas longas (sfc/dism/pnputil).
        services.AddSingleton<ElevatedScriptRunner>();
    }

    /// <summary>Serviços específicos do Windows: WMI, monitoramento, energia, rede, reparo.</summary>
    private static void AddWindowsServices(this IServiceCollection services)
    {
        services.AddSingleton<ISystemInformationService, WmiSystemInformationService>();
        services.AddSingleton<IPerformanceMonitor, WindowsPerformanceMonitor>();
        services.AddSingleton<IPowerPlanService, PowerPlanService>();
        services.AddSingleton<IDnsService, DnsService>();
        services.AddSingleton<INetworkToolsService, NetworkToolsService>();
        services.AddSingleton<ISystemRepairService, SystemRepairService>();
        services.AddSingleton<IDriverManagerService, DriverManagerService>();
        services.AddSingleton<IHostsEditorService, HostsEditorService>();
        services.AddSingleton<IBackupService, BackupService>();
    }

    /// <summary>
    /// Provedores de limpeza (Strategy). O <c>ICleanupService</c> da camada
    /// Application recebe <see cref="IEnumerable{T}"/> e compõe todos.
    /// </summary>
    private static void AddCleanupProviders(this IServiceCollection services)
    {
        services.AddSingleton<ICleanupProvider, FileSystemCleanupProvider>();
        services.AddSingleton<ICleanupProvider, BrowserCacheCleanupProvider>();
        services.AddSingleton<ICleanupProvider, RegistryCleanupProvider>();
    }

    /// <summary>Persistência EF Core + SQLite (contexto por operação) e repositórios.</summary>
    private static void AddPersistence(this IServiceCollection services)
    {
        var databasePath = ResolvePaths(services).DatabasePath;

        // O EF abre o arquivo diretamente: a pasta precisa existir antes do primeiro contexto.
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        services.AddDbContextFactory<HlOptimizerDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));

        services.AddSingleton<IActionHistoryRepository, ActionHistoryRepository>();
        services.AddSingleton<IScanReportRepository, ScanReportRepository>();
        services.AddSingleton<IGameModeRepository, GameModeRepository>();
    }

    /// <summary>Atualizações (API de releases do GitHub) e o HttpClient compartilhado.</summary>
    private static void AddUpdateServices(this IServiceCollection services)
    {
        // Sem Microsoft.Extensions.Http: um HttpClient singleton já é suficiente
        // (o aplicativo faz poucas requisições, só na verificação de updates).
        services.AddSingleton(_ =>
        {
            var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "HL-Pro-Optimizer");

            return httpClient;
        });

        services.AddSingleton<IUpdateService, GitHubUpdateService>();
    }

    /// <summary>Configura o pipeline de logging (Serilog em arquivo rotativo).</summary>
    private static void AddLoggingPipeline(this IServiceCollection services, bool verboseLogging)
    {
        var paths = ResolvePaths(services);

        var version = (Assembly.GetEntryAssembly() ?? typeof(InfrastructureServiceCollectionExtensions).Assembly)
            .GetName()
            .Version?
            .ToString() ?? "1.0.0";

        var serilogLogger = SerilogBootstrap.CreateLogger(paths, verboseLogging, version);

        // Log.Logger é o ponto de captura de exceções não tratadas no App.xaml.cs.
        global::Serilog.Log.Logger = serilogLogger;

        services.AddLogging(builder => builder
            .ClearProviders()
            .AddSerilog(serilogLogger, dispose: false)
            .SetMinimumLevel(verboseLogging ? LogLevel.Debug : LogLevel.Information));

        // Disponibiliza o logger do Serilog para quem precisar dele diretamente
        // (ex.: App.xaml.cs registrando exceções fatais fora do pipeline do DI).
        services.AddSingleton(serilogLogger);
    }

    /// <summary>
    /// Recupera o <see cref="ISystemPaths"/> já registrado (ou cria um provisório).
    /// </summary>
    /// <remarks>
    /// Provisório só acontece se alguém registrar a Infrastructure sem chamar
    /// <see cref="AddCorePlatform"/> — nesse caso os caminhos padrão do %AppData%
    /// continuam corretos, e nada explode no startup.
    /// </remarks>
    private static ISystemPaths ResolvePaths(IServiceCollection services) =>
        services.FirstOrDefault(s => s.ServiceType == typeof(ISystemPaths))?.ImplementationInstance as ISystemPaths
            ?? new SystemPaths();
}

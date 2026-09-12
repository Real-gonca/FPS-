using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Domain.Ports;
using Nexus.Infrastructure.Benchmark;
using Nexus.Infrastructure.Commands;
using Nexus.Infrastructure.Elevation;
using Nexus.Infrastructure.Persistence;
using Nexus.Infrastructure.Registry;
using Nexus.Infrastructure.Services;
using Nexus.Infrastructure.Storage;
using Nexus.Infrastructure.Telemetry;

namespace Nexus.Infrastructure;

public static class DependencyInjection
{
    public static string DefaultDataDirectory
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexusOptimizer");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static IServiceCollection AddNexusInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ── Persistência (EF Core + SQLite) ────────────────────────────────
        string? configuredPath = configuration["Nexus:DbPath"];
        string dbPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(DefaultDataDirectory, "nexus.db")
            : configuredPath;

        services.AddDbContext<NexusDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<IHistoryStore, EfHistoryStore>();
        services.AddSingleton<ITelemetryHistory, EfTelemetryHistory>();
        services.AddSingleton<ISettingsService, EfSettingsStore>();
        services.AddSingleton<IBenchmarkLog, EfBenchmarkLog>();
        services.AddSingleton<IChangeApplier, EfChangeApplier>();

        // ── Fontes de dados reais ─────────────────────────────────────────
        services.AddSingleton<PerformanceCounterSource>();
        services.AddSingleton<WmiSystemInfo>();
        services.AddSingleton<ISystemTelemetry, SystemTelemetryProvider>();
        services.AddSingleton<IStorageProbe, TempFolderStorageProbe>();
        services.AddSingleton<IServiceInventory, WmiServiceInventory>();
        services.AddSingleton<IServiceInspector, WmiServiceInspector>();
        services.AddSingleton<IBenchmarkRunner, LocalBenchmarkRunner>();

        // ── Sistema: registry, comandos, elevação ─────────────────────────
        services.AddSingleton<IRegistryAccess, WindowsRegistryAccess>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ISystemCommandExecutor, WhitelistedCommandExecutor>();
        services.AddSingleton<IElevationService, ElevationService>();

        // ── Inicialização da base ─────────────────────────────────────────
        services.AddHostedService<DatabaseInitializer>();

        return services;
    }
}

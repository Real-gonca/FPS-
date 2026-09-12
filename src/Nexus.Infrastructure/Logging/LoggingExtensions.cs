using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace Nexus.Infrastructure.Logging;

/// <summary>
/// Logging estruturado com Serilog (spec §2.2): rotação diária de ficheiros
/// (%LOCALAPPDATA%\NexusOptimizer\logs\nexus-YYYY-MM-DD.log), 7 ficheiros
/// retidos, níveis configuráveis em appsettings.json (secção "Serilog").
/// </summary>
public static class LoggingExtensions
{
    public static string LogsDirectory
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexusOptimizer", "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static IHostBuilder UseNexusLogging(this IHostBuilder builder) =>
        builder.UseSerilog((context, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(LogsDirectory, "nexus-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));
}

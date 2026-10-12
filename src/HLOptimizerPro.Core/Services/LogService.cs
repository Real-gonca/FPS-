using HLOptimizerPro.Core.Helpers;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Configuração do Serilog: log em ficheiro (diário, com rotação e retenção de 30 dias)
/// e no Output/Debug para desenvolvimento.
///
/// Política de privacidade: o log técnico fica SEMPRE em %AppData% local. Como o módulo
/// de privacidade pode reduzir telemetria do Windows, seria incoerente enviar telemetria
/// para a nuvem — e por isso nada é enviado (ver <c>AppSettings.TelemetryEnabled</c>).
/// </summary>
public static class LogService
{
    private static bool _initialized;

    /// <summary>Configura o Serilog global. Idempotente (seguro chamar mais de uma vez).</summary>
    public static void Configure()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            AppPaths.EnsureCreated();

            var template = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("App", AppConstants.ProductName)
                .Enrich.WithProperty("Version", AppConstants.Version)
                .WriteTo.Debug(outputTemplate: template)
                .WriteTo.File(
                    path: Path.Combine(AppPaths.Logs, "hl-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    fileSizeLimitBytes: 20 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    outputTemplate: template)
                .CreateLogger();

            Log.Information("=== {App} {Version} iniciado ({Os}) ===", AppConstants.ProductName, AppConstants.Version, Environment.OSVersion.VersionString);
        }
        catch (Exception ex)
        {
            // Nunca deixar a aplicação cair por causa do log (ex.: pasta sem permissões).
            System.Diagnostics.Debug.WriteLine($"Falha ao configurar o Serilog: {ex}");
        }
    }

    /// <summary>Cria a fábrica de log usada pela injeção de dependências.</summary>
    public static ILoggerFactory CreateLoggerFactory()
    {
        Configure();
        return LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    /// <summary>Descarrega buffers pendentes — chamado no encerramento da aplicação.</summary>
    public static void Shutdown()
    {
        try
        {
            Log.Information("=== {App} encerrado ===", AppConstants.ProductName);
            Log.CloseAndFlush();
        }
        catch
        {
            // Ignorar falhas no encerramento.
        }
    }

    /// <summary>Apaga logs com mais de <paramref name="days"/> dias (usado pela limpeza automática).</summary>
    public static int PurgeOldLogs(int days = 30)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(AppPaths.Logs)) return 0;

            var limit = DateTime.Now.AddDays(-days);
            foreach (var file in Directory.EnumerateFiles(AppPaths.Logs, "hl-*.log"))
            {
                if (File.GetLastWriteTime(file) < limit)
                {
                    File.Delete(file);
                    removed++;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Falha ao expurgar logs: {ex.Message}");
        }

        return removed;
    }
}

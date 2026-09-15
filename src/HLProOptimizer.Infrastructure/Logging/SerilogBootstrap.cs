using HLProOptimizer.Core.Abstractions;
using Serilog;
using Serilog.Events;

namespace HLProOptimizer.Infrastructure.Logging;

/// <summary>
/// Configuração central do Serilog (logging estruturado em arquivo).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que arquivo rotativo diário?</b> Um otimizador roda operações longas e
/// raras (SFC, DISM, limpeza) cujo diagnóstico só aparece depois. Rotação diária +
/// limite de 14 arquivos mantém ~2 semanas de histórico sem crescer sem limite.
/// </para>
/// <para>
/// <b><c>shared: true</c></b> é essencial: o processo elevado (segunda instância
/// lançada pelo <c>ElevationService</c>) escreve no MESMO arquivo que o processo
/// principal, sem conflito de lock.
/// </para>
/// <para>
/// O nível mínimo é <c>Information</c>; ruído do EF Core é elevado para
/// <c>Warning</c> para não poluir o log com consultas do histórico.
/// </para>
/// </remarks>
public static class SerilogBootstrap
{
    /// <summary>Prefixo dos arquivos de log.</summary>
    private const string LogFilePrefix = "hl-optimizer-";

    /// <summary>Quantidade de arquivos diários retidos.</summary>
    private const int RetainedFileCount = 14;

    /// <summary>Tamanho máximo por arquivo antes de rolar (5 MB).</summary>
    private const long FileSizeLimitBytes = 5L * 1024 * 1024;

    /// <summary>Template de saída (contexto de origem alinhado para leitura humana).</summary>
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}{NewLine}    {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Cria o logger global do aplicativo.
    /// </summary>
    /// <param name="paths">Caminhos do aplicativo (pasta de logs).</param>
    /// <param name="verbose">Se deve incluir nível Debug e saída no console (diagnóstico).</param>
    /// <param name="applicationVersion">Versão gravada em cada evento (contexto).</param>
    /// <returns>Logger configurado (atribuir a <c>Log.Logger</c>).</returns>
    public static ILogger CreateLogger(ISystemPaths paths, bool verbose = false, string? applicationVersion = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            // Ruído do EF Core/SQLite não ajuda a diagnosticar otimizações.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Data.Sqlite", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "HL PRO OPTIMIZER")
            .Enrich.WithProperty("Version", applicationVersion ?? "0.0.0")
            // Sem pacotes Enrichers extras: propriedades simples resolvem e não
            // adicionam dependência (o processo elevado escreve no mesmo arquivo).
            .Enrich.WithProperty("Machine", Environment.MachineName)
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.File(
                path: Path.Combine(paths.LogDirectory, $"{LogFilePrefix}.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCount,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                shared: true,
                encoding: System.Text.Encoding.UTF8,
                outputTemplate: OutputTemplate);

        if (verbose)
        {
            // Console só em diagnóstico (evita custo em produção).
            configuration = configuration.WriteTo.Debug(outputTemplate: OutputTemplate);
        }

        return configuration.CreateLogger();
    }

    /// <summary>Caminho do arquivo de log do dia (exibido na aba "Sobre").</summary>
    /// <param name="paths">Caminhos do aplicativo.</param>
    public static string GetActiveLogFilePath(ISystemPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // O Serilog sufixa o intervalo de rolagem no nome final: hl-optimizer-20260914.log
        return Path.Combine(paths.LogDirectory, $"{LogFilePrefix}{DateTime.Now:yyyyMMdd}.log");
    }
}

using System.Text;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Ações rápidas do Dashboard (3.2 — "Atalhos Rápidos"):
/// Limpeza Rápida, Modo Jogo, Restaurar Ponto e Logs do Sistema.
///
/// Cada atalho executa a operação real correspondente — nenhum é um botão decorativo.
/// </summary>
public sealed class QuickActionsService
{
    private readonly ILogger<QuickActionsService> _logger;
    private readonly CleanupService _cleanup;
    private readonly GameModeService _gameMode;
    private readonly RestorePointService _restorePoints;
    private readonly ActivityLogService _activity;
    private readonly OptimizationJournal _journal;
    private readonly SystemInfoService _systemInfo;
    private readonly MonitoringService _monitoring;
    private readonly SettingsService _settings;

    public QuickActionsService(
        ILogger<QuickActionsService> logger,
        CleanupService cleanup,
        GameModeService gameMode,
        RestorePointService restorePoints,
        ActivityLogService activity,
        OptimizationJournal journal,
        SystemInfoService systemInfo,
        MonitoringService monitoring,
        SettingsService settings)
    {
        _logger = logger;
        _cleanup = cleanup;
        _gameMode = gameMode;
        _restorePoints = restorePoints;
        _activity = activity;
        _journal = journal;
        _systemInfo = systemInfo;
        _monitoring = monitoring;
        _settings = settings;
    }

    /// <summary>1) Limpeza Rápida: temporários, cache de DNS e miniaturas.</summary>
    public async Task<Result> QuickCleanupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Limpeza rápida iniciada a partir do Dashboard.");
        var result = await _cleanup.RunAsync(CleanupTargets.QuickTargets(), cancellationToken).ConfigureAwait(false);

        return result.Success
            ? Result.Ok(result.Message)
            : Result.Fail(result.Message, result.TechnicalDetails);
    }

    /// <summary>2) Modo Jogo: liga/desliga a monitorização automática de jogos.</summary>
    public Result ToggleGameMode()
    {
        var enable = !_gameMode.IsEnabled;

        _settings.Update(s =>
        {
            s.GameModeEnabled = enable;
            s.GameModeAutoActivate = enable;
        });

        return _gameMode.SetEnabled(enable, autoActivate: true);
    }

    /// <summary>3) Restaurar Ponto: cria um ponto de restauro imediato.</summary>
    public async Task<Result> CreateRestorePointAsync(CancellationToken cancellationToken = default)
    {
        var result = await _restorePoints.CreateAsync($"{AppConstants.RestorePointDescription} — manual", cancellationToken)
            .ConfigureAwait(false);

        // Se o WMI falhar, tentamos a alternativa do PowerShell antes de desistir.
        if (!result.Success)
        {
            _logger.LogWarning("WMI falhou ao criar o ponto de restauro; a tentar PowerShell.");
            var fallback = await _restorePoints.CreateWithPowerShellAsync($"{AppConstants.RestorePointDescription} — manual", cancellationToken)
                .ConfigureAwait(false);

            if (fallback.Success) return fallback;
        }

        _activity.Log("Segurança", $"Ponto de restauro: {result.Message}", result.Success);
        return result;
    }

    /// <summary>4) Logs do Sistema: recolhe tudo o que é útil para suporte num único ficheiro.</summary>
    public async Task<Result<string>> ExportSystemLogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            AppPaths.EnsureCreated();

            var builder = new StringBuilder();
            var metrics = _monitoring.Read();

            builder.AppendLine($"{AppConstants.ProductName} {AppConstants.Version} — relatório de sistema");
            builder.AppendLine($"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            builder.AppendLine(new string('-', 72));

            builder.AppendLine();
            builder.AppendLine("## Hardware");
            builder.AppendLine(_systemInfo.Hardware.Summary);
            builder.AppendLine($"Identificador da máquina: {_systemInfo.MachineFingerprint}");

            builder.AppendLine();
            builder.AppendLine("## Sistema operativo");
            var report = _systemInfo.Report;
            builder.AppendLine($"{report.OsName} (build {report.OsBuild}, {report.Architecture})");
            builder.AppendLine($"Elevado: {(report.IsElevated ? "sim" : "não")} • Plano de energia: {report.PowerPlan}");
            builder.AppendLine($"Tempo ligado: {Formatters.Uptime(metrics.Uptime)}");

            builder.AppendLine();
            builder.AppendLine("## Métricas no momento");
            builder.AppendLine($"CPU: {Formatters.Percent(metrics.CpuUsage)} • RAM: {Formatters.Percent(metrics.Memory.Usage)} " +
                               $"({Formatters.Bytes(metrics.Memory.UsedBytes)} de {Formatters.Bytes(metrics.Memory.TotalBytes)})");
            builder.AppendLine($"GPU: {(metrics.GpuUsageAvailable ? Formatters.Percent(metrics.GpuUsage) : "não detetável")} • " +
                               $"Temperatura CPU: {Formatters.Temperature(metrics.CpuTemperatureCelsius)}");

            builder.AppendLine();
            builder.AppendLine("## Discos");
            foreach (var disk in _systemInfo.Disks)
            {
                builder.AppendLine($"{disk.Drive} {disk.VolumeLabel} — {Formatters.Bytes(disk.FreeBytes)} livres de " +
                                   $"{Formatters.Bytes(disk.TotalBytes)} ({disk.FreePercent:N1} %), {disk.MediaType}");
            }

            builder.AppendLine();
            builder.AppendLine("## Otimizações aplicadas (diário)");
            foreach (var entry in _journal.ActiveEntries)
            {
                builder.AppendLine($"[{entry.AppliedAt:dd/MM/yyyy HH:mm}] {entry.Title} ({entry.Category})" +
                                   (entry.RequiresRestart ? " — requer reinício" : string.Empty));
            }

            builder.AppendLine();
            builder.AppendLine("## Histórico de atividades (últimas 50)");
            foreach (var entry in _activity.Recent(50))
            {
                builder.AppendLine($"{entry.TimeText} [{entry.Category}] {entry.Description} — {entry.StatusText} {entry.Details}");
            }

            builder.AppendLine();
            builder.AppendLine("## Ficheiros de log (últimas 200 linhas)");
            try
            {
                if (File.Exists(AppPaths.TodayLogFile))
                {
                    var lines = File.ReadLines(AppPaths.TodayLogFile).TakeLast(200);
                    builder.AppendLine(string.Join(Environment.NewLine, lines));
                }
                else
                {
                    builder.AppendLine("(sem ficheiro de log para hoje)");
                }
            }
            catch (Exception ex)
            {
                builder.AppendLine($"(não foi possível ler o log: {ex.Message})");
            }

            builder.AppendLine();
            builder.AppendLine(new string('-', 72));
            builder.AppendLine("Este relatório foi gerado localmente e não contém telemetria enviada para servidores externos.");

            var path = Path.Combine(AppPaths.Reports, $"HL-Logs-{DateTime.Now:yyyyMMdd-HHmm}.txt");
            await File.WriteAllTextAsync(path, builder.ToString(), cancellationToken).ConfigureAwait(false);

            _activity.Log("Suporte", $"Relatório de logs exportado: {Path.GetFileName(path)}");
            return Result<string>.Ok(path, $"Logs exportados para {path}. Pode anexá-los ao pedido de suporte.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao exportar os logs do sistema.");
            return Result<string>.Fail("Não foi possível exportar os logs do sistema.", ex.Message);
        }
    }

    /// <summary>Abre a pasta de dados da aplicação no Explorador.</summary>
    public Result OpenDataFolder()
    {
        try
        {
            AppPaths.EnsureCreated();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.Root,
                UseShellExecute = true,
            });

            return Result.Ok($"Pasta aberta: {AppPaths.Root}");
        }
        catch (Exception ex)
        {
            return Result.Fail("Não foi possível abrir a pasta de dados.", ex.Message);
        }
    }
}

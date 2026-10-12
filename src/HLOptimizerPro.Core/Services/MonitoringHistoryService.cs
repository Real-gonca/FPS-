using System.Globalization;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Amostragem e persistência do histórico de métricas que alimenta o gráfico
/// "Desempenho do Sistema (24 h)".
///
/// Formato: JSONL de linhas compactas (timestamp|cpu|ram|gpu|temp) — uma amostra por minuto,
/// o que dá 1440 linhas/dia (~90 KB). O ficheiro é truncado às últimas 24 h de cada vez que é
/// carregado, para não crescer indefinidamente nem degradar o arranque.
/// </summary>
public sealed class MonitoringHistoryService
{
    private readonly ILogger<MonitoringHistoryService> _logger;
    private readonly IClock _clock;
    private readonly List<MetricSample> _samples = new();
    private readonly object _gate = new();

    private DateTime _lastSample = DateTime.MinValue;

    public MonitoringHistoryService(ILogger<MonitoringHistoryService> logger, IClock clock)
    {
        _logger = logger;
        _clock = clock;
        Load();
    }

    /// <summary>Número de amostras em memória (diagnóstico).</summary>
    public int SampleCount
    {
        get
        {
            lock (_gate) return _samples.Count;
        }
    }

    /// <summary>
    /// Regista uma amostra se já passou o intervalo configurado (1 min por omissão).
    /// Devolve true quando uma amostra nova foi efetivamente gravada.
    /// </summary>
    public bool TryRecord(SystemMetrics metrics, bool force = false)
    {
        var now = _clock.Now;

        if (!force && (now - _lastSample).TotalSeconds < AppConstants.HistorySampleSeconds) return false;
        _lastSample = now;

        var sample = new MetricSample
        {
            Timestamp = now,
            CpuPercent = Math.Round(metrics.CpuUsage * 100, 2),
            RamPercent = Math.Round(metrics.Memory.Usage * 100, 2),
            GpuPercent = metrics.GpuUsageAvailable ? Math.Round(metrics.GpuUsage * 100, 2) : 0,
            CpuTemperature = metrics.CpuTemperatureCelsius,
        };

        lock (_gate)
        {
            _samples.Add(sample);
            TrimWindow();
        }

        Append(sample);
        return true;
    }

    /// <summary>Devolve as amostras das últimas <paramref name="hours"/> horas, ordenadas.</summary>
    public IReadOnlyList<MetricSample> GetSamples(int hours = AppConstants.HistoryWindowHours)
    {
        var limit = _clock.Now.AddHours(-hours);
        lock (_gate)
        {
            return _samples.Where(s => s.Timestamp >= limit).OrderBy(s => s.Timestamp).ToList();
        }
    }

    /// <summary>Médias do período (usadas no relatório e no cartão de resumo do dashboard).</summary>
    public (double Cpu, double Ram, double Gpu, double? Temp) GetAverages(int hours = AppConstants.HistoryWindowHours)
    {
        var samples = GetSamples(hours);
        if (samples.Count == 0) return (0, 0, 0, null);

        var temps = samples.Where(s => s.CpuTemperature is > 0).Select(s => s.CpuTemperature!.Value).ToList();

        return (
            samples.Average(s => s.CpuPercent),
            samples.Average(s => s.RamPercent),
            samples.Average(s => s.GpuPercent),
            temps.Count > 0 ? temps.Average() : null);
    }

    /// <summary>Pico de CPU/RAM no período (ajuda a diagnosticar travamentos intermitentes).</summary>
    public (double Cpu, double Ram, DateTime? Timestamp) GetPeaks(int hours = AppConstants.HistoryWindowHours)
    {
        var samples = GetSamples(hours);
        if (samples.Count == 0) return (0, 0, null);

        var cpuPeak = samples.MaxBy(s => s.CpuPercent)!;
        var ramPeak = samples.MaxBy(s => s.RamPercent)!;
        return (cpuPeak.CpuPercent, ramPeak.RamPercent, cpuPeak.Timestamp);
    }

    /// <summary>Garante que a amostra de agora fica gravada (usado no encerramento da aplicação).</summary>
    public void Flush(SystemMetrics metrics) => TryRecord(metrics, force: true);

    /// <summary>Apaga o histórico (ação explícita do utilizador na Configuração).</summary>
    public Result Clear()
    {
        try
        {
            lock (_gate)
            {
                _samples.Clear();
            }

            if (File.Exists(AppPaths.MetricsHistoryFile)) File.Delete(AppPaths.MetricsHistoryFile);
            return Result.Ok("Histórico de desempenho apagado.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao apagar o histórico de desempenho.");
            return Result.Fail("Não foi possível apagar o histórico de desempenho.", ex.Message);
        }
    }

    // ------------------------------------------------------------------ persistência

    private void Load()
    {
        try
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(AppPaths.MetricsHistoryFile)) return;

            var limit = _clock.Now.AddHours(-AppConstants.HistoryWindowHours * 2);
            var loaded = new List<MetricSample>();

            foreach (var line in File.ReadLines(AppPaths.MetricsHistoryFile))
            {
                var sample = MetricSample.FromLine(line);
                if (sample is not null && sample.Timestamp >= limit) loaded.Add(sample);
            }

            lock (_gate)
            {
                _samples.Clear();
                _samples.AddRange(loaded.OrderBy(s => s.Timestamp));
            }

            _logger.LogInformation("Histórico de desempenho carregado: {Count} amostras.", loaded.Count);

            // Reescreve o ficheiro já truncado (mantém o crescimento sob controlo).
            Rewrite();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível carregar o histórico de desempenho — o gráfico começará vazio.");
        }
    }

    private void Append(MetricSample sample)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.AppendAllText(AppPaths.MetricsHistoryFile, sample.ToLine() + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao gravar a amostra de desempenho.");
        }
    }

    private void Rewrite()
    {
        try
        {
            List<MetricSample> copy;
            lock (_gate)
            {
                copy = _samples.ToList();
            }

            var lines = copy.Select(s => s.ToLine());
            File.WriteAllLines(AppPaths.MetricsHistoryFile, lines);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao reescrever o histórico de desempenho.");
        }
    }

    /// <summary>Mantém apenas a janela de 24 h (+ margem) em memória.</summary>
    private void TrimWindow()
    {
        var cutoff = _clock.Now.AddHours(-AppConstants.HistoryWindowHours * 2);
        _samples.RemoveAll(s => s.Timestamp < cutoff);

        // Limite duro de segurança (se o intervalo de amostragem for reduzido no futuro).
        if (_samples.Count > AppConstants.MetricsHistoryMaxLines * 2)
            _samples.RemoveRange(0, _samples.Count - AppConstants.MetricsHistoryMaxLines * 2);
    }

    /// <summary>Formata uma amostra para o tooltip/etiqueta do gráfico (pt-PT).</summary>
    public static string Describe(MetricSample sample) =>
        $"{sample.Timestamp:dd/MM HH:mm} — CPU {sample.CpuPercent.ToString("N0", CultureInfo.GetCultureInfo("pt-PT"))} % · " +
        $"RAM {sample.RamPercent.ToString("N0", CultureInfo.GetCultureInfo("pt-PT"))} % · " +
        $"GPU {sample.GpuPercent.ToString("N0", CultureInfo.GetCultureInfo("pt-PT"))} %";
}

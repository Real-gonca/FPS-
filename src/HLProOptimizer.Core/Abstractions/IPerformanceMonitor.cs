using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Coleta contínua de métricas em tempo real (CPU, RAM, GPU, disco, rede).
/// Mantém um histórico em <c>RingBuffer</c> para os gráficos dos últimos N segundos.
/// </summary>
public interface IPerformanceMonitor
{
    /// <summary>Disparado a cada amostra coletada (thread de background).</summary>
    event EventHandler<PerformanceSample>? SampleCollected;

    /// <summary>Indica se o monitor está em execução.</summary>
    bool IsRunning { get; }

    /// <summary>Intervalo de coleta atual.</summary>
    TimeSpan Interval { get; }

    /// <summary>Histórico de amostras (mais antigo primeiro).</summary>
    IReadOnlyList<PerformanceSample> History { get; }

    /// <summary>Inicia a coleta periódica.</summary>
    /// <param name="interval">Intervalo entre amostras (1s, 2s ou 5s).</param>
    /// <param name="historySeconds">Quantos segundos de histórico manter.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task StartAsync(TimeSpan interval, int historySeconds = 60, CancellationToken cancellationToken = default);

    /// <summary>Interrompe a coleta periódica.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Altera o intervalo em execução sem interromper o histórico.</summary>
    /// <param name="interval">Novo intervalo.</param>
    void SetInterval(TimeSpan interval);

    /// <summary>Coleta uma amostra avulsa (usada pelo Dashboard ao carregar).</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PerformanceSample> ReadSampleAsync(CancellationToken cancellationToken = default);

    /// <summary>Limpa o histórico (ex.: ao trocar de aba).</summary>
    void ClearHistory();
}

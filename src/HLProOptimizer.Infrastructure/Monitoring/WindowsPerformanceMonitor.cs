using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Monitoring;

/// <summary>
/// Monitor de desempenho em tempo real baseado em contadores de desempenho do
/// Windows (PDH) + chamadas nativas leves (<c>GlobalMemoryStatusEx</c>,
/// <c>GetPerformanceInfo</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que não WMI aqui?</b> WMI é ótimo para inventário estático
/// (<see cref="Windows.WmiSystemInformationService"/>), mas cada consulta leva
/// dezenas/centenas de milissegundos — inviável num laço de 1 segundo. Contadores
/// de desempenho são leitura de memória compartilhada do kernel e custam
/// microssegundos.
/// </para>
/// <para>
/// <b>Contadores são "delta"</b>: a primeira leitura de um contador de taxa
/// retorna 0; por isso <see cref="PrimeCounters"/> é chamado antes do laço.
/// </para>
/// <para>
/// <b>GPU</b>: a categoria "GPU Engine" expõe uma instância por
/// (processo × adaptador × engine). Filtramos <c>engtype_3D</c> (renderização 3D,
/// a que interessa para jogos), agrupamos por LUID do adaptador e usamos o maior
/// valor — o uso real do adaptador principal.
/// </para>
/// <para>Todas as leituras são defensivas: um contador ausente/negado vira 0 e é
/// registrado em Debug, nunca interrompe o monitoramento.</para>
/// </remarks>
public sealed partial class WindowsPerformanceMonitor : IPerformanceMonitor, IDisposable
{
    /// <summary>Máximo de instâncias de "GPU Engine" monitoradas (proteção contra máquinas com muitos processos).</summary>
    private const int MaxGpuEngineInstances = 400;

    /// <summary>A cada N amostras os contadores de GPU são reconstruídos (processos nascem/morrem).</summary>
    private const int GpuRefreshSamples = 60;

    /// <summary>A cada N amostras a temperatura é relida (WMI é caro).</summary>
    private const int TemperatureRefreshSamples = 10;

    /// <summary>A cada N amostras a latência de rede é medida.</summary>
    private const int LatencyRefreshSamples = 5;

    /// <summary>Timeout do ping de latência (ms).</summary>
    private const int PingTimeoutMilliseconds = 1500;

    private static readonly Regex LuidPattern = LuidRegex();

    private readonly ISystemInformationService _systemInformation;
    private readonly ISettingsService _settings;
    private readonly ILogger<WindowsPerformanceMonitor> _logger;

    private readonly SemaphoreSlim _collectLock = new(1, 1);
    private readonly object _historyLock = new();

    private RingBuffer<PerformanceSample> _history = new(60);
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private long _sampleIndex;
    private double _maxClockMHz;
    private bool _disposed;

    // --------- contadores ---------
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _cpuPerformanceCounter;
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;
    private PerformanceCounter? _diskTimeCounter;
    private Dictionary<string, List<PerformanceCounter>> _gpuEngineByAdapter = [];
    private List<PerformanceCounter> _gpuMemoryCounters = [];

    // --------- estado de deltas ---------
    private long _lastNetworkRxBytes;
    private long _lastNetworkTxBytes;
    private DateTime _lastNetworkSampleAt = DateTime.MinValue;
    private NetworkInterface[] _networkInterfaces = [];
    private double? _lastLatencyMs;
    private double? _lastCpuTemperature;
    private double? _lastGpuTemperature;

    /// <summary>Cria o monitor de desempenho.</summary>
    /// <param name="systemInformation">Inventário (clock máximo da CPU e temperaturas).</param>
    /// <param name="settings">Configurações (sonda de latência).</param>
    /// <param name="logger">Logger.</param>
    public WindowsPerformanceMonitor(
        ISystemInformationService systemInformation,
        ISettingsService settings,
        ILogger<WindowsPerformanceMonitor> logger)
    {
        _systemInformation = systemInformation;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<PerformanceSample>? SampleCollected;

    /// <inheritdoc />
    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <inheritdoc />
    public TimeSpan Interval => _interval;

    /// <inheritdoc />
    public IReadOnlyList<PerformanceSample> History
    {
        get
        {
            lock (_historyLock)
            {
                return _history.ToList();
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(TimeSpan interval, int historySeconds = 60, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning)
        {
            _logger.LogDebug("Monitor já está em execução; ignorando StartAsync duplicado.");
            return;
        }

        _interval = NormalizeInterval(interval);

        var capacity = Math.Max(10, (int)Math.Ceiling(historySeconds / _interval.TotalSeconds));

        lock (_historyLock)
        {
            _history = new RingBuffer<PerformanceSample>(capacity);
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;

        _loopTask = Task.Run(() => RunLoopAsync(token), CancellationToken.None);

        _logger.LogInformation(
            "Monitor de desempenho iniciado (intervalo {Interval}s, histórico {History}s / {Capacity} amostras).",
            _interval.TotalSeconds,
            historySeconds,
            capacity);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var cts = _loopCts;
        var loop = _loopTask;

        _loopCts = null;
        _loopTask = null;

        if (cts is null || loop is null)
        {
            return;
        }

        try
        {
            await cts.CancelAsync().ConfigureAwait(false);

            // Aguarda no máximo 2s: o laço pode estar no meio de uma coleta.
            await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelamento esperado.
        }
        finally
        {
            cts.Dispose();
            _logger.LogInformation("Monitor de desempenho interrompido após {Samples} amostras.", _sampleIndex);
        }
    }

    /// <inheritdoc />
    public void SetInterval(TimeSpan interval)
    {
        var normalized = NormalizeInterval(interval);

        if (normalized == _interval)
        {
            return;
        }

        _interval = normalized;

        _logger.LogInformation("Intervalo do monitor alterado para {Interval}s.", normalized.TotalSeconds);
    }

    /// <inheritdoc />
    public Task<PerformanceSample> ReadSampleAsync(CancellationToken cancellationToken = default)
        => CollectAsync(cancellationToken);

    /// <inheritdoc />
    public void ClearHistory()
    {
        lock (_historyLock)
        {
            _history.Clear();
        }
    }

    /// <summary>Libera contadores e cancela o laço.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _loopCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Já liberado.
        }

        _loopCts?.Dispose();
        _loopCts = null;

        DisposeCounters();
        _collectLock.Dispose();
    }

    // ---------------------------------------------------------------------
    // Inicialização
    // ---------------------------------------------------------------------

    /// <summary>Cria os contadores e faz a leitura de aquecimento.</summary>
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("Contadores de desempenho só existem no Windows; o monitor retornará zeros.");
            return;
        }

        try
        {
            _maxClockMHz = (await _systemInformation.GetSystemProfileAsync(cancellationToken).ConfigureAwait(false)).Cpu.MaxClockMHz;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível obter o clock máximo da CPU.");
        }

        _cpuCounter = CreateCounter("Processor", "_Total", "% Processor Time");
        _cpuPerformanceCounter = CreateCounter("Processor Information", "_Total", "% Processor Performance");
        _diskReadCounter = CreateCounter("PhysicalDisk", "_Total", "Disk Read Bytes/sec");
        _diskWriteCounter = CreateCounter("PhysicalDisk", "_Total", "Disk Write Bytes/sec");
        _diskTimeCounter = CreateCounter("PhysicalDisk", "_Total", "% Disk Time");

        RefreshNetworkInterfaces();
        InitializeGpuCounters();
        PrimeCounters();
    }

    /// <summary>Cria um contador individualmente (retorna <c>null</c> quando indisponível).</summary>
    private PerformanceCounter? CreateCounter(string category, string instance, string counter)
    {
        try
        {
            var performanceCounter = new PerformanceCounter(category, counter, instance, readOnly: true);

            // Força a resolução imediata: se a categoria não existir, falha aqui (e não no laço).
            _ = performanceCounter.NextValue();

            return performanceCounter;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Contador indisponível: {Category}\\{Instance}\\{Counter}.", category, instance, counter);
            return null;
        }
    }

    /// <summary>Primeira leitura de todos os contadores (estabelece a linha de base dos deltas).</summary>
    private void PrimeCounters()
    {
        NextValueSafe(_cpuCounter);
        NextValueSafe(_cpuPerformanceCounter);
        NextValueSafe(_diskReadCounter);
        NextValueSafe(_diskWriteCounter);
        NextValueSafe(_diskTimeCounter);

        foreach (var counters in _gpuEngineByAdapter.Values)
        {
            foreach (var counter in counters)
            {
                NextValueSafe(counter);
            }
        }

        foreach (var counter in _gpuMemoryCounters)
        {
            NextValueSafe(counter);
        }

        _lastNetworkRxBytes = ReadNetworkTotals(out var txBytes);
        _lastNetworkTxBytes = txBytes;
        _lastNetworkSampleAt = DateTime.Now;
    }

    /// <summary>Aquece apenas os contadores de GPU (não zera o baseline de rede/disco).</summary>
    private void PrimeGpuCounters()
    {
        foreach (var counters in _gpuEngineByAdapter.Values)
        {
            foreach (var counter in counters)
            {
                NextValueSafe(counter);
            }
        }

        foreach (var counter in _gpuMemoryCounters)
        {
            NextValueSafe(counter);
        }
    }

    /// <summary>Constrói (ou reconstrói) os contadores de GPU.</summary>
    private void InitializeGpuCounters()
    {
        try
        {
            DisposeGpuCounters();

            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                _logger.LogDebug("Categoria 'GPU Engine' indisponível; uso de GPU ficará em 0.");
                return;
            }

            var engineCounters = new Dictionary<string, List<PerformanceCounter>>(StringComparer.OrdinalIgnoreCase);
            var taken = 0;

            foreach (var instance in PerformanceCounterCategory.GetInstanceNames("GPU Engine"))
            {
                if (taken >= MaxGpuEngineInstances)
                {
                    break;
                }

                // Só o engine 3D representa carga de renderização (jogos/D3D).
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var adapterKey = LuidPattern.Match(instance).Value;

                if (adapterKey.Length == 0)
                {
                    adapterKey = "default";
                }

                try
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);

                    if (!engineCounters.TryGetValue(adapterKey, out var list))
                    {
                        list = [];
                        engineCounters[adapterKey] = list;
                    }

                    list.Add(counter);
                    taken++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Instância de GPU ignorada: {Instance}.", instance);
                }
            }

            _gpuEngineByAdapter = engineCounters;

            if (PerformanceCounterCategory.Exists("GPU Adapter Memory"))
            {
                foreach (var instance in PerformanceCounterCategory.GetInstanceNames("GPU Adapter Memory"))
                {
                    try
                    {
                        _gpuMemoryCounters.Add(new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", instance, readOnly: true));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Instância de memória de GPU ignorada: {Instance}.", instance);
                    }
                }
            }

            _logger.LogDebug(
                "Contadores de GPU prontos: {Engines} instâncias 3D em {Adapters} adaptador(es), {Memory} de VRAM.",
                taken,
                _gpuEngineByAdapter.Count,
                _gpuMemoryCounters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao inicializar os contadores de GPU.");
        }
    }

    private void DisposeGpuCounters()
    {
        foreach (var counters in _gpuEngineByAdapter.Values)
        {
            foreach (var counter in counters)
            {
                counter.Dispose();
            }
        }

        foreach (var counter in _gpuMemoryCounters)
        {
            counter.Dispose();
        }

        _gpuEngineByAdapter = [];
        _gpuMemoryCounters = [];
    }

    private void DisposeCounters()
    {
        _cpuCounter?.Dispose();
        _cpuPerformanceCounter?.Dispose();
        _diskReadCounter?.Dispose();
        _diskWriteCounter?.Dispose();
        _diskTimeCounter?.Dispose();
        DisposeGpuCounters();

        _cpuCounter = null;
        _cpuPerformanceCounter = null;
        _diskReadCounter = null;
        _diskWriteCounter = null;
        _diskTimeCounter = null;
    }

    // ---------------------------------------------------------------------
    // Laço de coleta
    // ---------------------------------------------------------------------

    /// <summary>Laço principal: coleta, publica e aguarda o próximo intervalo.</summary>
    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var sample = await CollectAsync(cancellationToken).ConfigureAwait(false);

                SampleCollected?.Invoke(this, sample);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // O laço nunca morre por uma falha de contador: só registra e segue.
                _logger.LogError(ex, "Falha na coleta da amostra #{Index}.", _sampleIndex);
            }

            var remaining = _interval - stopwatch.Elapsed;

            if (remaining > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Coleta uma amostra completa (thread-safe: só uma coleta por vez).</summary>
    private async Task<PerformanceSample> CollectAsync(CancellationToken cancellationToken)
    {
        await _collectLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _sampleIndex++;

            if (!OperatingSystem.IsWindows())
            {
                return PerformanceSample.Empty with { Timestamp = DateTime.Now };
            }

            if (_sampleIndex % GpuRefreshSamples == 0)
            {
                InitializeGpuCounters();
                PrimeGpuCounters();
            }

            if (_sampleIndex % 30 == 0)
            {
                RefreshNetworkInterfaces();
            }

            var timestamp = DateTime.Now;

            var cpuPercent = ClampPercent(NextValueSafe(_cpuCounter));
            var cpuClockGHz = ReadCpuClockGHz();
            var (memoryPercent, memoryInUse, memoryAvailable) = ReadMemory();
            var (gpuPercent, gpuMemory) = ReadGpu();
            var (diskRead, diskWrite) = ReadDiskThroughput();
            var diskPercent = ClampPercent(NextValueSafe(_diskTimeCounter));
            var (download, upload) = ReadNetworkThroughput(timestamp);
            var (processCount, threadCount) = ReadProcessCounts();

            await RefreshTemperatureAsync(timestamp, cancellationToken).ConfigureAwait(false);
            await RefreshLatencyAsync(timestamp, cancellationToken).ConfigureAwait(false);

            var sample = new PerformanceSample(
                Timestamp: timestamp,
                CpuPercent: cpuPercent,
                CpuTemperatureCelsius: _lastCpuTemperature,
                CpuClockGHz: cpuClockGHz,
                MemoryPercent: memoryPercent,
                MemoryInUseBytes: memoryInUse,
                MemoryAvailableBytes: memoryAvailable,
                GpuPercent: gpuPercent,
                GpuTemperatureCelsius: _lastGpuTemperature,
                GpuMemoryUsedBytes: gpuMemory,
                DiskReadBytesPerSecond: diskRead,
                DiskWriteBytesPerSecond: diskWrite,
                DiskPercent: diskPercent,
                NetworkDownloadBytesPerSecond: download,
                NetworkUploadBytesPerSecond: upload,
                NetworkLatencyMs: _lastLatencyMs,
                ProcessCount: processCount,
                ThreadCount: threadCount);

            lock (_historyLock)
            {
                _history.Add(sample);
            }

            return sample;
        }
        finally
        {
            _collectLock.Release();
        }
    }

    /// <summary>Clock efetivo = % Processor Performance × clock máximo.</summary>
    private double ReadCpuClockGHz()
    {
        var performance = NextValueSafe(_cpuPerformanceCounter);

        if (performance <= 0 || _maxClockMHz <= 0)
        {
            return 0d;
        }

        return Math.Round(performance / 100d * _maxClockMHz / 1000d, 2);
    }

    /// <summary>Uso de memória via <c>GlobalMemoryStatusEx</c> (custa microssegundos).</summary>
    private (double Percent, long InUseBytes, long AvailableBytes) ReadMemory()
    {
        var status = new NativeMethods.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
        };

        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            return (0d, 0L, 0L);
        }

        var total = (long)status.ullTotalPhys;
        var available = (long)status.ullAvailPhys;
        var inUse = Math.Max(0, total - available);

        return (ClampPercent(Percentage.Of(inUse, total)), inUse, available);
    }

    /// <summary>Uso de GPU (maior adaptador) e VRAM dedicada total.</summary>
    private (double Percent, long DedicatedBytes) ReadGpu()
    {
        var percent = 0d;

        foreach (var counters in _gpuEngineByAdapter.Values)
        {
            var adapterPercent = 0d;

            foreach (var counter in counters)
            {
                adapterPercent = Math.Max(adapterPercent, NextValueSafe(counter));
            }

            percent = Math.Max(percent, adapterPercent);
        }

        long dedicated = 0;

        foreach (var counter in _gpuMemoryCounters)
        {
            dedicated += (long)NextValueSafe(counter);
        }

        return (ClampPercent(percent), dedicated);
    }

    /// <summary>Throughput de disco (bytes/s).</summary>
    private (double Read, double Write) ReadDiskThroughput()
        => (Math.Max(0, NextValueSafe(_diskReadCounter)), Math.Max(0, NextValueSafe(_diskWriteCounter)));

    /// <summary>Throughput de rede calculado pela diferença acumulada dos adaptadores ativos.</summary>
    private (double Download, double Upload) ReadNetworkThroughput(DateTime timestamp)
    {
        var rx = ReadNetworkTotals(out var tx);

        var elapsed = _lastNetworkSampleAt == DateTime.MinValue
            ? 0d
            : (timestamp - _lastNetworkSampleAt).TotalSeconds;

        double download = 0;
        double upload = 0;

        // elapsed == 0 na primeira amostra; contadores reiniciam se a placa for reativada.
        if (elapsed > 0.05)
        {
            download = Math.Max(0, rx - _lastNetworkRxBytes) / elapsed;
            upload = Math.Max(0, tx - _lastNetworkTxBytes) / elapsed;
        }

        _lastNetworkRxBytes = rx;
        _lastNetworkTxBytes = tx;
        _lastNetworkSampleAt = timestamp;

        return (download, upload);
    }

    private void RefreshNetworkInterfaces()
    {
        try
        {
            _networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .Where(ni => ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao enumerar as interfaces de rede.");
            _networkInterfaces = [];
        }
    }

    private long ReadNetworkTotals(out long txBytes)
    {
        long rx = 0;
        long tx = 0;

        foreach (var networkInterface in _networkInterfaces)
        {
            try
            {
                var statistics = networkInterface.GetIPStatistics();

                rx += statistics.BytesReceived;
                tx += statistics.BytesSent;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Estatísticas indisponíveis para {Interface}.", networkInterface.Name);
            }
        }

        txBytes = tx;

        return rx;
    }

    /// <summary>Contagem de processos e threads via <c>GetPerformanceInfo</c>.</summary>
    private (int Processes, int Threads) ReadProcessCounts()
    {
        var info = new NativeMethods.PERFORMANCE_INFORMATION
        {
            cb = Marshal.SizeOf<NativeMethods.PERFORMANCE_INFORMATION>()
        };

        return NativeMethods.GetPerformanceInfo(out info, info.cb)
            ? (info.ProcessCount, info.ThreadCount)
            : (Process.GetProcesses().Length, 0);
    }

    /// <summary>Relê a temperatura apenas em amostras pontuais (WMI é caro demais para 1s).</summary>
    private async Task RefreshTemperatureAsync(DateTime timestamp, CancellationToken cancellationToken)
    {
        var isScheduledSample = _sampleIndex == 1 || _sampleIndex % TemperatureRefreshSamples == 0;

        // Sem sensor conhecido, reduz ainda mais a frequência (1 tentativa a cada 30 amostras).
        var isRetryWithoutSensor = _lastCpuTemperature is null && _sampleIndex % 30 == 0;

        if (!isScheduledSample && !isRetryWithoutSensor)
        {
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));

            var temperature = await _systemInformation.GetTemperatureAsync(timeout.Token).ConfigureAwait(false);

            _lastCpuTemperature = temperature.CpuCelsius;
            _lastGpuTemperature = temperature.GpuCelsius;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Leitura de temperatura expirou em {Timestamp}.", timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao ler a temperatura.");
        }
    }

    /// <summary>Mede a latência de rede (ping) quando habilitada nas configurações.</summary>
    private async Task RefreshLatencyAsync(DateTime timestamp, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;

        if (!settings.MeasureNetworkLatency || string.IsNullOrWhiteSpace(settings.LatencyProbeHost))
        {
            _lastLatencyMs = null;
            return;
        }

        if (_sampleIndex % LatencyRefreshSamples != 0)
        {
            return;
        }

        try
        {
            using var ping = new Ping();

            // SendPingAsync não aceita CancellationToken no .NET 8: corremos o ping contra
            // um "delay cancelável" vinculado ao token do laço. O CTS local é descartado ao
            // final para não acumular timers registrados no token de longa duração.
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var pingTask = ping.SendPingAsync(settings.LatencyProbeHost, PingTimeoutMilliseconds);
            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, delayCts.Token);

            var completed = await Task.WhenAny(pingTask, cancellationTask).ConfigureAwait(false);

            // Libera o timer imediatamente e observa a exceção da tarefa descartada.
            await delayCts.CancelAsync().ConfigureAwait(false);
            ObserveTaskException(cancellationTask);

            if (completed != pingTask)
            {
                ObserveTaskException(pingTask);
                _logger.LogDebug("Ping para {Host} cancelado em {Timestamp}.", settings.LatencyProbeHost, timestamp);
                return;
            }

            var reply = await pingTask.ConfigureAwait(false);

            _lastLatencyMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Ping para {Host} expirou em {Timestamp}.", settings.LatencyProbeHost, timestamp);
        }
        catch (Exception ex)
        {
            // Host inalcançável não é erro do monitor.
            _logger.LogDebug(ex, "Falha ao medir a latência para {Host}.", settings.LatencyProbeHost);
        }
    }

    // ---------------------------------------------------------------------
    // Utilitários
    // ---------------------------------------------------------------------

    /// <summary>Lê um contador sem propagar exceções (instâncias podem sumir).</summary>
    private static float NextValueSafe(PerformanceCounter? counter)
    {
        if (counter is null)
        {
            return 0f;
        }

        try
        {
            return counter.NextValue();
        }
        catch (Exception)
        {
            return 0f;
        }
    }

    /// <summary>
    /// Observa a exceção de uma tarefa abandonada, evitando
    /// <c>UnobservedTaskException</c> quando ela falha após o descarte.
    /// </summary>
    private static void ObserveTaskException(Task task) =>
        _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static double ClampPercent(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : 0d;

    private static TimeSpan NormalizeInterval(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(1);
        }

        return TimeSpan.FromSeconds(Math.Clamp(interval.TotalSeconds, 1, 60));
    }

    [GeneratedRegex("luid_0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+", RegexOptions.Compiled)]
    private static partial Regex LuidRegex();
}

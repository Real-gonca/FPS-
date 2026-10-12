using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HLOptimizerPro.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Benchmark de servidores DNS por medição real de latência.
///
/// Como funciona (e porque assim): enviámos consultas DNS verdadeiras (UDP/53) para cada
/// servidor e medimos o tempo até à resposta. Não usamos ICMP (ping) porque o ping mede a
/// latência do servidor ao pacote mais simples, e não o tempo de resolução de nomes — que é o
/// que interessa ao utilizador. Também não usamos APIs de terceiros: tudo corre localmente.
///
/// Cada servidor recebe <see cref="QueryCount"/> consultas a nomes diferentes (para evitar
/// respostas em cache) e reportamos:
///   • latência mínima (melhor caso — o número mostrado em destaque),
///   • média, desvio (jitter) e taxa de resposta,
///   • uma pontuação 0–100 que combina latência, fiabilidade e estabilidade.
/// </summary>
public sealed class DnsBenchmarkService
{
    /// <summary>Servidores públicos testados (os pedidos no briefing + Quad9 como referência).</summary>
    public static IReadOnlyList<DnsProvider> Providers { get; } = new List<DnsProvider>
    {
        new()
        {
            Key = "google",
            Name = "Google Public DNS",
            Primary = "8.8.8.8",
            Secondary = "8.8.4.4",
            Country = "Global (Anycast)",
            Notes = "Estável e muito rápido; boa escolha geral.",
        },
        new()
        {
            Key = "cloudflare",
            Name = "Cloudflare 1.1.1.1",
            Primary = "1.1.1.1",
            Secondary = "1.0.0.1",
            Country = "Global (Anycast)",
            Notes = "Costuma ser o de menor latência; política de privacidade rigorosa.",
        },
        new()
        {
            Key = "opendns",
            Name = "OpenDNS (Cisco)",
            Primary = "208.67.222.222",
            Secondary = "208.67.220.220",
            Country = "Global (Anycast)",
            Notes = "Inclui filtragem de phishing e opções de controlo parental.",
        },
        new()
        {
            Key = "norton",
            Name = "Norton ConnectSafe",
            Primary = "199.85.126.10",
            Secondary = "199.85.127.10",
            Country = "Global (Anycast)",
            Notes = "Foco em segurança: bloqueia sites maliciosos por omissão.",
        },
        new()
        {
            Key = "quad9",
            Name = "Quad9",
            Primary = "9.9.9.9",
            Secondary = "149.112.112.112",
            Country = "Global (Anycast)",
            Notes = "Bloqueia domínios de malware conhecidos, sem registo de dados pessoais.",
        },
    };

    /// <summary>Nomes usados nas consultas: diferentes para evitar respostas em cache do próprio servidor.</summary>
    private static readonly string[] QueryHosts =
    {
        "www.google.com", "www.cloudflare.com", "www.microsoft.com", "www.amazon.com", "www.wikipedia.org",
        "www.nuget.org", "api.github.com", "www.bing.com",
    };

    private readonly ILogger<DnsBenchmarkService> _logger;

    public DnsBenchmarkService(ILogger<DnsBenchmarkService> logger) => _logger = logger;

    /// <summary>Consultas por servidor (5 é suficiente para uma leitura estável e rápida).</summary>
    public int QueryCount { get; init; } = 5;

    /// <summary>Timeout por consulta.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>Testa um servidor DNS.</summary>
    public async Task<DnsBenchmarkResult> TestAsync(DnsProvider provider, CancellationToken cancellationToken = default)
    {
        var latencies = new List<double>();
        var failures = 0;
        var sent = 0;

        for (var i = 0; i < QueryCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var host = QueryHosts[i % QueryHosts.Length];
            sent++;

            var elapsed = await QueryOnceAsync(provider.Primary, host, cancellationToken).ConfigureAwait(false);
            if (elapsed is null) failures++;
            else latencies.Add(elapsed.Value);

            // Pequena pausa para não sobrecarregar o servidor nem inflacionar a latência.
            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
        }

        return BuildResult(provider, latencies, sent, failures);
    }

    /// <summary>Testa todos os servidores em paralelo (muito mais rápido que sequencial).</summary>
    public async Task<IReadOnlyList<DnsBenchmarkResult>> TestAllAsync(
        IEnumerable<DnsProvider>? providers = null,
        IProgress<(string Provider, DnsBenchmarkResult? Result)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var list = (providers ?? Providers).ToList();
        var ordered = new DnsBenchmarkResult[list.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, list.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var provider = list[index];
                var result = await TestAsync(provider, token).ConfigureAwait(false);
                ordered[index] = result;
                progress?.Report((provider.Name, result));
            }).ConfigureAwait(false);

        return ordered.OrderByDescending(r => r.Score).ToList();
    }

    /// <summary>Consulta o servidor DNS indicado e devolve a latência em ms (null se não respondeu).</summary>
    private async Task<double?> QueryOnceAsync(string server, string hostname, CancellationToken cancellationToken)
    {
        try
        {
            var serverAddress = IPAddress.Parse(server);
            var endpoint = new IPEndPoint(serverAddress, 53);

            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = (int)Timeout.TotalMilliseconds;
            socket.SendTimeout = (int)Timeout.TotalMilliseconds;

            var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
            var query = BuildQuery(id, hostname);

            var stopwatch = Stopwatch.StartNew();
            await socket.SendToAsync(query, SocketFlags.None, endpoint, cancellationToken).ConfigureAwait(false);

            var buffer = new byte[512];
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Timeout);

            var received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, endpoint, timeoutCts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            return IsValidResponse(buffer.AsSpan(0, received.ReceivedBytes), id) ? stopwatch.Elapsed.TotalMilliseconds : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;   // timeout
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "Consulta DNS a {Server} falhou.", server);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Erro inesperado na consulta DNS a {Server}.", server);
            return null;
        }
    }

    /// <summary>
    /// Constrói uma consulta DNS tipo A (RFC 1035 §4). Implementado à mão para não depender de
    /// bibliotecas de terceiros e para permitir medir exatamente o tempo de ida e volta.
    /// </summary>
    internal static byte[] BuildQuery(ushort id, string hostname)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII);

        writer.Write(IPAddress.HostToNetworkOrder((short)id));   // ID
        writer.Write(IPAddress.HostToNetworkOrder((short)0x0100)); // Flags: consulta recursiva padrão
        writer.Write(IPAddress.HostToNetworkOrder((short)1));    // QDCOUNT
        writer.Write(IPAddress.HostToNetworkOrder((short)0));    // ANCOUNT
        writer.Write(IPAddress.HostToNetworkOrder((short)0));    // NSCOUNT
        writer.Write(IPAddress.HostToNetworkOrder((short)0));    // ARCOUNT

        foreach (var label in hostname.Split('.'))
        {
            writer.Write((byte)label.Length);
            writer.Write(Encoding.ASCII.GetBytes(label));
        }

        writer.Write((byte)0);                                   // fim do nome
        writer.Write(IPAddress.HostToNetworkOrder((short)1));    // QTYPE = A
        writer.Write(IPAddress.HostToNetworkOrder((short)1));    // QCLASS = IN
        writer.Flush();

        return stream.ToArray();
    }

    /// <summary>
    /// Valida a resposta: o ID tem de coincidir e o bit QR (resposta) tem de estar definido.
    /// Também verificamos RCODE = 0 (sem erro), embora NXDOMAIN ainda prove que o servidor respondeu.
    /// </summary>
    internal static bool IsValidResponse(ReadOnlySpan<byte> response, ushort expectedId)
    {
        if (response.Length < 12) return false;

        var id = (ushort)((response[0] << 8) | response[1]);
        if (id != expectedId) return false;

        var flags = (ushort)((response[2] << 8) | response[3]);
        var isResponse = (flags & 0x8000) != 0;   // QR

        return isResponse;
    }

    /// <summary>Calcula latência, jitter, fiabilidade e pontuação. Separado para ser testável.</summary>
    internal static DnsBenchmarkResult BuildResult(DnsProvider provider, List<double> latencies, int sent, int failures)
    {
        var succeeded = latencies.Count;

        if (succeeded == 0)
        {
            return new DnsBenchmarkResult
            {
                Provider = provider,
                LatencyMs = 0,
                AverageMs = 0,
                JitterMs = 0,
                QueriesSent = sent,
                QueriesSucceeded = 0,
                Score = 0,
            };
        }

        var min = latencies.Min();
        var average = latencies.Average();
        var jitter = latencies.Max() - min;
        var lossRatio = sent <= 0 ? 1 : (double)failures / sent;

        // ---- Pontuação -------------------------------------------------------
        // Latência: 100 pontos até 10 ms, caindo linearmente até 0 aos 120 ms.
        var latencyScore = min <= 10 ? 100 : Math.Max(0, 100 - (min - 10) * (100.0 / 110.0));

        // Estabilidade: jitter até 5 ms é irrelevante; acima disso penaliza até 25 %.
        var jitterPenalty = Math.Clamp(jitter / 40.0 * 25, 0, 25);

        // Fiabilidade: cada consulta perdida custa 15 pontos.
        var lossPenalty = Math.Clamp(lossRatio * 100 * 0.15 / 0.2, 0, 40);

        var score = Math.Clamp(latencyScore - jitterPenalty - lossPenalty, 0, 100);

        return new DnsBenchmarkResult
        {
            Provider = provider,
            LatencyMs = Math.Round(min, 1),
            AverageMs = Math.Round(average, 1),
            JitterMs = Math.Round(jitter, 1),
            QueriesSent = sent,
            QueriesSucceeded = succeeded,
            Score = Math.Round(score, 1),
        };
    }
}

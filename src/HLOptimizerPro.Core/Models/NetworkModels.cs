namespace HLOptimizerPro.Core.Models;

/// <summary>Informação da ligação de rede ativa (módulo Rede).</summary>
public sealed class NetworkAdapterInfo
{
    public required string Name { get; init; }

    /// <summary>GUID da interface (usado para localizar a chave de registo de cada adaptador).</summary>
    public string Id { get; init; } = string.Empty;
    public required string Description { get; init; }
    public string AdapterType { get; init; } = "Ethernet";     // Ethernet / Wi-Fi / VPN
    public string MacAddress { get; init; } = string.Empty;
    public long SpeedBitsPerSecond { get; init; }
    public bool IsUp { get; init; }
    public bool IsWireless { get; init; }
    public string IPv4 { get; init; } = "—";
    public string SubnetMask { get; init; } = "—";
    public string Gateway { get; init; } = "—";
    public string Ipv6 { get; init; } = "—";
    public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string>();
    public bool DhcpEnabled { get; init; } = true;

    public string SpeedText => SpeedBitsPerSecond <= 0
        ? "—"
        : SpeedBitsPerSecond >= 1_000_000_000
            ? $"{SpeedBitsPerSecond / 1_000_000_000d:N1} Gbps"
            : $"{SpeedBitsPerSecond / 1_000_000d:N0} Mbps";

    public string DnsText => DnsServers.Count == 0 ? "Automático (DHCP)" : string.Join(", ", DnsServers);
}

/// <summary>Servidor DNS público testável no benchmark.</summary>
public sealed class DnsProvider
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Primary { get; init; }
    public string Secondary { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public bool IsCurrent { get; set; }

    public IReadOnlyList<string> Servers =>
        string.IsNullOrWhiteSpace(Secondary) ? new[] { Primary } : new[] { Primary, Secondary };

    public string ServersText => string.Join("  •  ", Servers);
}

/// <summary>Resultado do benchmark de um servidor DNS (100 % baseado em medições reais).</summary>
public sealed class DnsBenchmarkResult
{
    public required DnsProvider Provider { get; init; }

    /// <summary>Latência mínima das consultas válidas (ms) — é o valor mostrado em destaque.</summary>
    public double LatencyMs { get; init; }

    /// <summary>Latência média das consultas válidas (ms).</summary>
    public double AverageMs { get; init; }

    /// <summary>Amplitude (jitter) entre a melhor e a pior consulta válida.</summary>
    public double JitterMs { get; init; }

    public int QueriesSent { get; init; }
    public int QueriesSucceeded { get; init; }
    public double PacketLossPercent => QueriesSent <= 0 ? 100 : 100.0 * (QueriesSent - QueriesSucceeded) / QueriesSent;

    /// <summary>Pontuação 0–100 combinando latência, fiabilidade e estabilidade.</summary>
    public double Score { get; init; }

    public bool IsReachable => QueriesSucceeded > 0;

    public string LatencyText => IsReachable ? Helpers.Formatters.Milliseconds(LatencyMs) : "Sem resposta";
    public string ReliabilityText => $"{QueriesSucceeded}/{QueriesSent} respostas ({100 - PacketLossPercent:N0} %)";
    public string ScoreText => IsReachable ? Helpers.Formatters.Score(Score) : "—";
    public string StatusText => !IsReachable ? "Inacessível"
        : Score >= 85 ? "Excelente"
        : Score >= 70 ? "Bom"
        : Score >= 50 ? "Regular"
        : "Fraco";

    public HealthLevel Health => !IsReachable ? HealthLevel.Critico
        : Score >= 85 ? HealthLevel.Otimo
        : Score >= 60 ? HealthLevel.Atencao
        : HealthLevel.Critico;
}

/// <summary>Relatório do módulo Rede reunindo ligação + benchmark.</summary>
public sealed class NetworkReport
{
    public NetworkAdapterInfo? ActiveAdapter { get; init; }
    public bool InternetAvailable { get; init; }
    public string PublicIpAddress { get; init; } = "—";
    public string Isp { get; init; } = "—";
    public double GatewayLatencyMs { get; init; }
    public double InternetLatencyMs { get; init; }
    public IReadOnlyList<DnsBenchmarkResult> DnsResults { get; init; } = Array.Empty<DnsBenchmarkResult>();

    /// <summary>Melhor servidor segundo as medições (o recomendado a aplicar).</summary>
    public DnsBenchmarkResult? BestDns =>
        DnsResults.Where(r => r.IsReachable).OrderByDescending(r => r.Score).ThenBy(r => r.LatencyMs).FirstOrDefault();

    /// <summary>True quando o DNS atual já não é o melhor medido nem está entre os 10 % melhores.</summary>
    public bool DnsChangeRecommended
    {
        get
        {
            var best = BestDns;
            if (best is null || ActiveAdapter is null) return false;

            var current = DnsResults.FirstOrDefault(r => r.Provider.IsCurrent);
            return current is null || current.Provider.Key != best.Provider.Key;
        }
    }
}

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Módulo de Rede (3.4b): estado da ligação, adaptador, DNS, benchmark e aplicação de DNS.
///
/// Todas as operações usam as ferramentas oficiais do Windows (<c>netsh</c>, <c>ipconfig</c>) em
/// vez de escrever diretamente no registo dos adaptadores. Motivo: o registo é atualizado de
/// forma assíncrona e outros serviços (DHCP, VPN, Wi-Fi) podem sobrepor-se — via <c>netsh</c> a
/// alteração é aplicada corretamente e o Windows notifica os componentes de rede.
///
/// Reversibilidade: antes de trocar o DNS guardamos os servidores atuais (ver
/// <see cref="GetConfiguredDns"/>), o que permite repor "Automático (DHCP)" a qualquer momento.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NetworkService
{
    private readonly ILogger<NetworkService> _logger;
    private readonly IProcessRunner _runner;
    private readonly DnsBenchmarkService _dnsBenchmark;
    private readonly ActivityLogService _activity;
    private readonly HttpClient _httpClient;

    public NetworkService(
        ILogger<NetworkService> logger,
        IProcessRunner runner,
        DnsBenchmarkService dnsBenchmark,
        ActivityLogService activity)
    {
        _logger = logger;
        _runner = runner;
        _dnsBenchmark = dnsBenchmark;
        _activity = activity;

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppConstants.ProductName}/{AppConstants.Version}");
    }

    /// <summary>Adaptadores ativos (exclui loopback, túneis e adaptadores virtuais desligados).</summary>
    public IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                var props = nic.GetIPProperties();
                var ipv4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

                var gateway = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;

                var dns = props.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString())
                    .ToList();

                var isWireless = nic.NetworkInterfaceType is NetworkInterfaceType.Wireless80211;
                var dhcp = IsDhcpEnabled(nic.Name);

                adapters.Add(new NetworkAdapterInfo
                {
                    Name = nic.Name,
                    Id = nic.Id,
                    Description = nic.Description,
                    AdapterType = isWireless ? "Wi-Fi" : nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet || nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet
                        ? "Ethernet"
                        : nic.NetworkInterfaceType.ToString(),
                    MacAddress = FormatMac(nic.GetPhysicalAddress().ToString()),
                    SpeedBitsPerSecond = SafeSpeed(nic),
                    IsUp = true,
                    IsWireless = isWireless,
                    IPv4 = ipv4?.Address.ToString() ?? "—",
                    SubnetMask = ipv4?.IPv4Mask?.ToString() ?? "—",
                    Gateway = gateway?.ToString() ?? "—",
                    Ipv6 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)?.Address.ToString() ?? "—",
                    DnsServers = dns,
                    DhcpEnabled = dhcp,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível enumerar os adaptadores de rede.");
        }

        return adapters;
    }

    /// <summary>Adaptador principal (Ethernet ativa com gateway tem prioridade sobre Wi-Fi).</summary>
    public NetworkAdapterInfo? GetPrimaryAdapter()
        => GetAdapters()
            .OrderByDescending(a => a.AdapterType == "Ethernet")
            .ThenByDescending(a => a.Gateway != "—")
            .FirstOrDefault();

    /// <summary>Relatório completo: ligação + latências + benchmark de DNS.</summary>
    public async Task<NetworkReport> AnalyzeAsync(
        bool runDnsBenchmark = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("A ler os adaptadores…");
        var adapter = GetPrimaryAdapter();

        progress?.Report("A testar a ligação…");
        var internetLatency = await MeasureLatencyAsync("1.1.1.1", cancellationToken).ConfigureAwait(false);
        var gatewayLatency = adapter?.Gateway is { } gateway && gateway != "—"
            ? await MeasureLatencyAsync(gateway, cancellationToken).ConfigureAwait(false)
            : 0;

        progress?.Report("A obter endereço público…");
        var publicIp = await GetPublicIpAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<DnsBenchmarkResult> dnsResults = Array.Empty<DnsBenchmarkResult>();

        if (runDnsBenchmark)
        {
            progress?.Report("A testar servidores DNS…");
            var providers = ProvidersWithCurrentFlag(adapter).ToList();

            dnsResults = await _dnsBenchmark.TestAllAsync(
                providers,
                new Progress<(string Provider, DnsBenchmarkResult? Result)>(p => progress?.Report($"DNS: {p.Provider} testado")),
                cancellationToken).ConfigureAwait(false);
        }

        return new NetworkReport
        {
            ActiveAdapter = adapter,
            InternetAvailable = internetLatency > 0,
            PublicIpAddress = publicIp,
            GatewayLatencyMs = gatewayLatency,
            InternetLatencyMs = internetLatency,
            DnsResults = dnsResults,
        };
    }

    /// <summary>Marca os fornecedores conhecidos cujo servidor já está configurado no adaptador.</summary>
    private static IEnumerable<DnsProvider> ProvidersWithCurrentFlag(NetworkAdapterInfo? adapter)
    {
        var configured = adapter?.DnsServers ?? (IReadOnlyList<string>)Array.Empty<string>();

        foreach (var provider in DnsBenchmarkService.Providers)
        {
            provider.IsCurrent = provider.Servers.Any(s => configured.Contains(s));
            yield return provider;
        }
    }

    /// <summary>Mede a latência de ida e volta a um destino (ICMP) com 4 amostras.</summary>
    public async Task<double> MeasureLatencyAsync(string host, CancellationToken cancellationToken = default, int samples = 4)
    {
        try
        {
            using var ping = new Ping();
            var values = new List<long>();

            for (var i = 0; i < samples; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var reply = await ping.SendPingAsync(host, 1000).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success) values.Add(reply.RoundtripTime);

                await Task.Delay(60, cancellationToken).ConfigureAwait(false);
            }

            return values.Count == 0 ? 0 : values.Average();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ping a {Host} falhou.", host);
            return 0;
        }
    }

    /// <summary>Endereço IP público (usado apenas quando há Internet; nunca é enviado para servidores nossos).</summary>
    public async Task<string> GetPublicIpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _httpClient.GetStringAsync("https://api.ipify.org", cancellationToken).ConfigureAwait(false);
            return value.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível obter o IP público.");
            return "—";
        }
    }

    /// <summary>
    /// Aplica servidores DNS ao adaptador indicado.
    /// Devolve também o DNS anterior, guardado no backup para reversão.
    /// </summary>
    public Result ApplyDns(NetworkAdapterInfo adapter, DnsProvider provider, bool flushDns, bool renewIp, bool forceDhcp)
    {
        try
        {
            var interfaceName = adapter.Name;
            var previous = adapter.DnsServers.Count > 0 ? string.Join(", ", adapter.DnsServers) : "Automático (DHCP)";
            var success = true;
            var output = new List<string>();

            if (forceDhcp)
            {
                // Repor modo automático (DHCP).
                var reset = _runner.Run("netsh", $"interface ip set dns name=\"{interfaceName}\" source=dhcp");
                success &= reset.Success;
                output.Add(reset.Output);
            }
            else
            {
                var primary = _runner.Run("netsh", $"interface ip set dns name=\"{interfaceName}\" static {provider.Primary} primary");
                success &= primary.Success;
                output.Add(primary.Output);

                if (!string.IsNullOrWhiteSpace(provider.Secondary))
                {
                    var secondary = _runner.Run("netsh", $"interface ip add dns name=\"{interfaceName}\" {provider.Secondary} index=2");
                    // Falha do secundário não invalida o primário.
                    output.Add(secondary.Output);
                }
            }

            if (!success)
            {
                _activity.Log("Rede", $"Falha ao aplicar DNS {provider.Name}", success: false, details: string.Join(" | ", output));
                return Result.Fail("Não foi possível aplicar o DNS (verifique se a aplicação está elevada e se o adaptador aceita alterações).",
                    string.Join("\n", output));
            }

            if (flushDns) FlushDns();
            if (renewIp) RenewIp(interfaceName);

            var message = forceDhcp
                ? $"DNS de «{interfaceName}» reposto em modo automático (antes: {previous})."
                : $"DNS de «{interfaceName}» definido para {provider.Name} ({provider.ServersText}). Valores anteriores: {previous}.";

            _activity.Log("Rede", message);
            return Result.Ok(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao aplicar DNS.");
            return Result.Fail("Erro ao aplicar o DNS.", ex.Message);
        }
    }

    /// <summary>Limpa a cache de DNS (ipconfig /flushdns).</summary>
    public Result FlushDns()
    {
        var result = _runner.Run("ipconfig", "/flushdns");
        return result.Success
            ? Result.Ok("Cache de DNS limpa.")
            : Result.Fail("Não foi possível limpar a cache de DNS.", result.Output);
    }

    /// <summary>Renova o endereço IP do adaptador (ipconfig /renew).</summary>
    public Result RenewIp(string? adapterName = null)
    {
        var arguments = adapterName is null ? "/renew" : $"/renew \"{adapterName}\"";
        var result = _runner.Run("ipconfig", arguments);

        return result.Success
            ? Result.Ok("Endereço IP renovado.")
            : Result.Fail("Não foi possível renovar o IP (pode demorar alguns segundos — tente novamente).", result.Output);
    }

    /// <summary>Repõe a pilha TCP/IP e o Winsock (resolve problemas de rede persistentes; requer reinício).</summary>
    public Result ResetNetworkStack()
    {
        var winsock = _runner.Run("netsh", "winsock reset");
        var tcpip = _runner.Run("netsh", "int ip reset");

        return winsock.Success && tcpip.Success
            ? Result.Ok("Pilha de rede reposta. É necessário reiniciar o computador para concluir.")
            : Result.Fail("Não foi possível repor a pilha de rede.", $"{winsock.Output}\n{tcpip.Output}");
    }

    /// <summary>Otimiza parâmetros de TCP para menor latência (com reversão documentada).</summary>
    public Result OptimizeTcpForLatency()
    {
        var operations = new (string Arguments, string Description)[]
        {
            ("int tcp set global autotuninglevel=normal", "Nível de autotuning TCP normalizado"),
            ("int tcp set global rss=enabled", "Receção lateral (RSS) ativada"),
            ("int tcp set global ecncapability=enabled", "ECN ativado (menos retransmissões)"),
            ("int tcp set heuristics disabled", "Heurísticas de janela TCP desativadas"),
            ("int tcp set supplemental Internet congestionprovider=ctcp", "Algoritmo de congestão CTCP"),
        };

        var results = operations.Select(op => _runner.Run("netsh", op.Arguments)).ToList();
        var failed = results.Count(r => !r.Success);

        return failed == 0
            ? Result.Ok("Parâmetros de TCP otimizados para menor latência.")
            : Result.Fail($"{failed} de {operations.Length} ajustes de TCP falharam (alguns parâmetros não existem em todas as versões do Windows).",
                string.Join("\n", results.Where(r => !r.Success).Select(r => r.Output)));
    }

    // ------------------------------------------------------------------ internos

    /// <summary>Deteta se o adaptador usa DHCP (via netsh, mais fiável que o registo).</summary>
    private bool IsDhcpEnabled(string interfaceName)
    {
        try
        {
            var result = _runner.Run("netsh", $"interface ip show config name=\"{interfaceName}\"");
            if (!result.Success) return true;

            return result.Output.Contains("DHCP enabled: Yes", StringComparison.OrdinalIgnoreCase)
                || result.Output.Contains("DHCP ativado: Sim", StringComparison.OrdinalIgnoreCase)
                || result.Output.Contains("DHCP: Yes", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>DNS atualmente configurado num adaptador (para backup antes de alterar).</summary>
    public IReadOnlyList<string> GetConfiguredDns(string interfaceName)
    {
        try
        {
            var result = _runner.Run("netsh", $"interface ip show dns name=\"{interfaceName}\"");
            if (!result.Success) return Array.Empty<string>();

            return result.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains(":", StringComparison.Ordinal))
                .Select(line => line[(line.IndexOf(':') + 1)..].Trim())
                .Where(value => IPAddress.TryParse(value, out _))
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static long SafeSpeed(NetworkInterface nic)
    {
        try
        {
            return nic.Speed;
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatMac(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "—";
        return string.Join(":", Enumerable.Range(0, raw.Length / 2).Select(i => raw.Substring(i * 2, 2).ToUpperInvariant()));
    }
}

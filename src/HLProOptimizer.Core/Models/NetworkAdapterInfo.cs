namespace HLProOptimizer.Core.Models;

/// <summary>Adaptador de rede e sua configuração IP/DNS.</summary>
/// <param name="Name">Nome da conexão (ex.: "Ethernet").</param>
/// <param name="Description">Descrição do driver.</param>
/// <param name="MacAddress">Endereço físico.</param>
/// <param name="LinkSpeedMbps">Velocidade de link negociada.</param>
/// <param name="Ipv4Address">Endereço IPv4 principal.</param>
/// <param name="Ipv6Address">Endereço IPv6 principal.</param>
/// <param name="DnsServers">Servidores DNS configurados.</param>
/// <param name="Gateway">Gateway padrão.</param>
/// <param name="IsPhysical">Se é um adaptador físico (não loopback/tunnel).</param>
/// <param name="IsOperational">Se está conectado.</param>
/// <param name="IsDhcpEnabled">Se obtém IP via DHCP.</param>
public sealed record NetworkAdapterInfo(
    string Name,
    string Description,
    string MacAddress,
    double LinkSpeedMbps,
    string Ipv4Address,
    string Ipv6Address,
    IReadOnlyList<string> DnsServers,
    string Gateway,
    bool IsPhysical,
    bool IsOperational,
    bool IsDhcpEnabled);

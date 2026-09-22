using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net;

namespace HL.Optimizer.Pro.Core.Services;

public class NetworkService : INetworkService
{
    public async Task<NetworkInfo> GetNetworkInfoAsync()
    {
        return await Task.Run(async () =>
        {
            var info = new NetworkInfo();
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                var active = interfaces.FirstOrDefault();
                if (active != null)
                {
                    info.AdapterName = active.Name;
                    info.Speed = $"{active.Speed / 1_000_000} Mbps";
                    info.IsConnected = true;

                    var props = active.GetIPProperties();
                    var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipv4 != null)
                    {
                        info.IpAddress = ipv4.Address.ToString();
                    }

                    var gateway = props.GatewayAddresses.FirstOrDefault();
                    if (gateway != null)
                    {
                        info.Gateway = gateway.Address.ToString();
                    }

                    var dns = props.DnsAddresses.Where(d => d.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList();
                    if (dns.Count > 0) info.DnsPrimary = dns[0].ToString();
                    if (dns.Count > 1) info.DnsSecondary = dns[1].ToString();
                }

                info.LatencyMs = await PingAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Network info error: {ex.Message}");
            }
            return info;
        });
    }

    public async Task<double> PingAsync(string host = "8.8.8.8")
    {
        return await Task.Run(async () =>
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 3000);
                if (reply.Status == IPStatus.Success)
                    return (double)reply.RoundtripTime;
                return -1;
            }
            catch { return -1; }
        });
    }

    public async Task FlushDnsAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch { }
        });
    }

    public async Task ResetWinsockAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "winsock reset",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);
            }
            catch { }
        });
    }

    public async Task SetDnsAsync(string primary, string secondary)
    {
        await Task.Run(() =>
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                foreach (var ni in interfaces)
                {
                    try
                    {
                        var psi1 = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"interface ip set dns \"{ni.Name}\" static {primary}",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var p1 = Process.Start(psi1);
                        p1?.WaitForExit(5000);

                        if (!string.IsNullOrEmpty(secondary))
                        {
                            var psi2 = new ProcessStartInfo
                            {
                                FileName = "netsh",
                                Arguments = $"interface ip add dns \"{ni.Name}\" {secondary} index=2",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var p2 = Process.Start(psi2);
                            p2?.WaitForExit(5000);
                        }
                        break; // only first active
                    }
                    catch { }
                }
            }
            catch { }
        });
    }

    public async Task<List<string>> GetDnsServersAsync()
    {
        var info = await GetNetworkInfoAsync();
        var list = new List<string>();
        if (!string.IsNullOrEmpty(info.DnsPrimary)) list.Add(info.DnsPrimary);
        if (!string.IsNullOrEmpty(info.DnsSecondary)) list.Add(info.DnsSecondary);
        return list;
    }
}

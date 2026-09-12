using System.Management;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Services;

/// <summary>
/// Inventário de serviços do Windows via WMI (Win32_Service) — fonte real
/// (spec §4.6). Timeout de 3 s; qualquer falha → lista vazia + log
/// (a UI mostra "indisponível", nunca uma lista fabricada).
/// </summary>
public sealed class WmiServiceInventory : IServiceInventory
{
    private static readonly TimeSpan WmiTimeout = TimeSpan.FromSeconds(3);

    private readonly ILogger<WmiServiceInventory> _log;

    public WmiServiceInventory(ILogger<WmiServiceInventory> log) => _log = log;

    public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows())
                return Array.Empty<ServiceInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT Name, DisplayName, State, StartMode, StartName FROM Win32_Service",
                    new EnumerationOptions { Timeout = WmiTimeout });
                using var results = searcher.Get();

                var list = new List<ServiceInfo>();
                foreach (ManagementObject mo in results)
                {
                    list.Add(new ServiceInfo(
                        mo["Name"]?.ToString() ?? string.Empty,
                        mo["DisplayName"]?.ToString() ?? string.Empty,
                        mo["State"]?.ToString() ?? "Unknown",
                        mo["StartMode"]?.ToString() ?? "Unknown",
                        mo["StartName"]?.ToString() ?? string.Empty));
                }
                return list.AsReadOnly();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Inventário WMI de serviços falhou → lista vazia (a UI mostra indisponível).");
                return Array.Empty<ServiceInfo>();
            }
        }, ct);
}

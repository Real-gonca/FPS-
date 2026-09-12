using System.Management;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Services;

/// <summary>
/// Probe de um serviço individual (WMI Win32_Service by Name) — usado pelo
/// ChangeApplier para criar backups reais antes de alterar start mode/state.
/// </summary>
public sealed class WmiServiceInspector : IServiceInspector
{
    private static readonly TimeSpan WmiTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger<WmiServiceInspector> _log;

    public WmiServiceInspector(ILogger<WmiServiceInspector> log) => _log = log;

    public Task<ServiceProbe?> ProbeServiceAsync(string name, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows())
                return null;

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    $"SELECT Name, DisplayName, State, StartMode FROM Win32_Service WHERE Name = '{name.Replace("'", "''")}'",
                    new EnumerationOptions { Timeout = WmiTimeout });
                using var results = searcher.Get();

                foreach (ManagementObject mo in results)
                {
                    return new ServiceProbe(
                        mo["Name"]?.ToString() ?? name,
                        mo["DisplayName"]?.ToString() ?? string.Empty,
                        Exists: true,
                        mo["StartMode"]?.ToString(),
                        mo["State"]?.ToString());
                }
                return new ServiceProbe(name, string.Empty, Exists: false, null, null);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Probe WMI do serviço {Name} falhou → tratado como inexistente.", name);
                return null;
            }
        }, ct);
}

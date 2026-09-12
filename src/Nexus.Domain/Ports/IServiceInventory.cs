namespace Nexus.Domain.Ports;

/// <summary>
/// Serviço Windows (fonte real: WMI Win32_Service).
/// StartMode segue o vocabulário do WMI: Auto, Manual, Demand, Disabled,
/// Boot, System, Unknown. State: Running, Stopped, Paused, Start Pending,
/// Stop Pending, Continue Pending, Pause Pending.
/// </summary>
public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    string State,
    string StartMode,
    string StartName);

/// <summary>Probe de um único serviço (para backup de alterações — spec §2.2).</summary>
public sealed record ServiceProbe(
    string Name,
    string DisplayName,
    bool Exists,
    string? StartMode,
    string? State);

public interface IServiceInventory
{
    /// <summary>Lista todos os serviços. Falha → lista vazia (a UI mostra o erro, nunca fabrica).</summary>
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken ct = default);
}

public interface IServiceInspector
{
    /// <summary>Estado atual de um serviço por nome curto. null = serviço inexistente.</summary>
    Task<ServiceProbe?> ProbeServiceAsync(string name, CancellationToken ct = default);
}

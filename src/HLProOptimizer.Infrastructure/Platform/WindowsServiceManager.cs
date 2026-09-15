using System.ServiceProcess;
using HLProOptimizer.Application.Optimization;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;
using SystemManagement = System.Management;

namespace HLProOptimizer.Infrastructure.Platform;

/// <summary>
/// Gerenciador de serviços Windows: usa WMI (<c>Win32_Service</c>) para
/// inventário (start mode, delayed start, descrição, PID) e
/// <see cref="ServiceController"/> para controlar o ciclo de vida.
/// </summary>
public sealed class WindowsServiceManager : IServiceManager
{
    private const string ServiceQuery =
        "SELECT Name, DisplayName, State, StartMode, StartName, Description, ProcessId, PathName, DelayedAutoStart FROM Win32_Service";

    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);

    private readonly ICommandRunner _commandRunner;
    private readonly ILogger<WindowsServiceManager> _logger;

    /// <summary>Cria o gerenciador de serviços.</summary>
    /// <param name="commandRunner">Executor usado pelo <c>sc.exe</c> (alteração de start type).</param>
    /// <param name="logger">Logger.</param>
    public WindowsServiceManager(ICommandRunner commandRunner, ILogger<WindowsServiceManager> logger)
    {
        _commandRunner = commandRunner;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WindowsServiceInfo>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<WindowsServiceInfo>>(() =>
        {
            var result = new List<WindowsServiceInfo>();

            try
            {
                using var searcher = new SystemManagement.ManagementObjectSearcher(ServiceQuery);

                foreach (SystemManagement.ManagementBaseObject managementObject in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (managementObject)
                    {
                        result.Add(FromWmi(managementObject));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Falha na consulta WMI de serviços; usando ServiceController como fallback.");

                foreach (var controller in ServiceController.GetServices())
                {
                    using (controller)
                    {
                        result.Add(new WindowsServiceInfo
                        {
                            ServiceName = controller.ServiceName,
                            DisplayName = controller.DisplayName,
                            State = MapState(controller.Status),
                            CanStop = CanStop(controller),
                            IsCritical = ServiceOptimizationCatalog.CriticalServices
                                .Contains(controller.ServiceName, StringComparer.OrdinalIgnoreCase)
                        });
                    }
                }
            }

            return result
                .OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WindowsServiceInfo?> GetServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return null;
        }

        var services = await GetServicesAsync(cancellationToken).ConfigureAwait(false);

        return services.FirstOrDefault(s =>
            string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public Task<bool> StartAsync(string serviceName, CancellationToken cancellationToken = default)
        => ControlAsync(serviceName, ServiceAction.Start, cancellationToken);

    /// <inheritdoc />
    public Task<bool> StopAsync(string serviceName, CancellationToken cancellationToken = default)
        => ControlAsync(serviceName, ServiceAction.Stop, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> RestartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        await StopAsync(serviceName, cancellationToken).ConfigureAwait(false);

        return await StartAsync(serviceName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> SetStartupKindAsync(string serviceName, ServiceStartupKind startupKind, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return false;
        }

        var startValue = startupKind switch
        {
            ServiceStartupKind.Automatic => "auto",
            ServiceStartupKind.AutomaticDelayed => "delayed-auto",
            ServiceStartupKind.Manual => "demand",
            ServiceStartupKind.Disabled => "disabled",
            _ => "demand"
        };

        var elevated = !ElevationHelper.IsProcessElevated();

        var result = await _commandRunner.RunAsync(
            "sc.exe",
            $"config \"{serviceName}\" start= {startValue}",
            cancellationToken,
            elevated).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Serviço '{Service}' configurado como {Startup}.", serviceName, startupKind);
            return true;
        }

        _logger.LogWarning(
            "Falha ao configurar o serviço '{Service}' ({Startup}): {Error}",
            serviceName, startupKind, result.CombinedOutput);

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> IsRunningAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        var service = await GetServiceAsync(serviceName, cancellationToken).ConfigureAwait(false);

        return service?.IsRunning ?? false;
    }

    /// <summary>Executa uma ação de ciclo de vida com espera de status.</summary>
    private async Task<bool> ControlAsync(string serviceName, ServiceAction action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                using var controller = new ServiceController(serviceName);
                controller.Refresh();

                switch (action)
                {
                    case ServiceAction.Start:
                        if (controller.Status == ServiceControllerStatus.Running)
                        {
                            return true;
                        }

                        controller.Start();
                        controller.WaitForStatus(ServiceControllerStatus.Running, OperationTimeout);
                        break;

                    case ServiceAction.Stop:
                        if (controller.Status == ServiceControllerStatus.Stopped)
                        {
                            return true;
                        }

                        if (!CanStop(controller))
                        {
                            _logger.LogWarning("O serviço '{Service}' não pode ser parado.", serviceName);
                            return false;
                        }

                        controller.Stop();
                        controller.WaitForStatus(ServiceControllerStatus.Stopped, OperationTimeout);
                        break;
                }

                _logger.LogInformation("Serviço '{Service}' {Action} com sucesso.", serviceName, action);

                return true;
            }
            catch (System.InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Serviço '{Service}' não encontrado ou inacessível.", serviceName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao {Action} o serviço '{Service}'.", action, serviceName);
                return false;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Converte um objeto WMI em <see cref="WindowsServiceInfo"/>.</summary>
    private static WindowsServiceInfo FromWmi(SystemManagement.ManagementBaseObject managementObject)
    {
        var name = GetString(managementObject, "Name");
        var startMode = GetString(managementObject, "StartMode");
        var delayed = GetBool(managementObject, "DelayedAutoStart");

        return new WindowsServiceInfo
        {
            ServiceName = name,
            DisplayName = GetString(managementObject, "DisplayName"),
            Description = GetString(managementObject, "Description"),
            State = ParseState(GetString(managementObject, "State")),
            StartupKind = ParseStartupKind(startMode, delayed),
            ProcessId = (int)GetUInt32(managementObject, "ProcessId"),
            CanStop = string.Equals(startMode, "Disabled", StringComparison.OrdinalIgnoreCase) is false,
            CanPauseAndContinue = false,
            IsCritical = ServiceOptimizationCatalog.CriticalServices
                .Contains(name, StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>Converte StartMode + DelayedAutoStart do WMI para o enum do domínio.</summary>
    private static ServiceStartupKind ParseStartupKind(string startMode, bool delayed) => startMode switch
    {
        "Auto" => delayed ? ServiceStartupKind.AutomaticDelayed : ServiceStartupKind.Automatic,
        "Manual" => ServiceStartupKind.Manual,
        "Disabled" => ServiceStartupKind.Disabled,
        "Boot" or "System" => ServiceStartupKind.Automatic,
        _ => ServiceStartupKind.Unknown
    };

    /// <summary>Converte o State do WMI para o enum do domínio.</summary>
    private static ServiceState ParseState(string state) => state switch
    {
        "Running" => ServiceState.Running,
        "Stopped" => ServiceState.Stopped,
        "Start Pending" => ServiceState.StartPending,
        "Stop Pending" => ServiceState.StopPending,
        "Pause Pending" => ServiceState.StopPending,
        "Paused" => ServiceState.Paused,
        "Continue Pending" => ServiceState.ContinuePending,
        _ => ServiceState.Unknown
    };

    /// <summary>Converte o status do ServiceController para o enum do domínio.</summary>
    private static ServiceState MapState(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ServiceState.Running,
        ServiceControllerStatus.Stopped => ServiceState.Stopped,
        ServiceControllerStatus.StartPending => ServiceState.StartPending,
        ServiceControllerStatus.StopPending => ServiceState.StopPending,
        ServiceControllerStatus.Paused => ServiceState.Paused,
        ServiceControllerStatus.PausePending => ServiceState.StopPending,
        ServiceControllerStatus.ContinuePending => ServiceState.ContinuePending,
        _ => ServiceState.Unknown
    };

    private static bool CanStop(ServiceController controller)
    {
        try
        {
            return controller.CanStop;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string GetString(SystemManagement.ManagementBaseObject managementObject, string property)
        => managementObject[property]?.ToString() ?? string.Empty;

    private static bool GetBool(SystemManagement.ManagementBaseObject managementObject, string property)
    {
        var value = managementObject[property];

        return value switch
        {
            bool boolean => boolean,
            string text => bool.TryParse(text, out var parsed) && parsed,
            _ => false
        };
    }

    private static uint GetUInt32(SystemManagement.ManagementBaseObject managementObject, string property)
    {
        var value = managementObject[property];

        return value switch
        {
            uint unsigned => unsigned,
            int signed => (uint)Math.Max(0, signed),
            string text when uint.TryParse(text, out var parsed) => parsed,
            _ => 0u
        };
    }

    /// <summary>Ações de ciclo de vida suportadas.</summary>
    private enum ServiceAction
    {
        Start,
        Stop
    }
}

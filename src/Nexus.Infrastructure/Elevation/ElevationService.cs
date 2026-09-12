using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Elevation;

/// <summary>
/// Elevação de administrador (spec §2.2) com fallback elegante.
///
/// DECISÃO (docs/DECISIONS.md): o manifest usa asInvoker (não
/// requireAdministrator) porque com requireAdministrator, ao recusar o UAC o
/// processo nem chega a arrancar — não existiria o "fallback elegante".
/// Com asInvoker a app abre sempre, degrada para MODO LIMITADO (só leitura +
/// N/D, ações de sistema bloqueadas com explicação) e oferece relançamento
/// elevado (runas) a partir da UI.
/// </summary>
public sealed class ElevationService : IElevationService
{
    private readonly ILogger<ElevationService> _log;

    public bool IsElevated { get; }

    public ElevationService(ILogger<ElevationService> log)
    {
        _log = log;
        IsElevated = DetectElevation();
        _log.LogInformation("Elevação: {State}.",
            IsElevated ? "a correr como administrador" : "a correr sem privilégios (modo limitado)");
    }

    private static bool DetectElevation()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return false;

            using var identity = WindowsIdentity.GetOwner();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public Task<bool> RequestElevationAsync()
    {
        if (IsElevated)
            return Task.FromResult(true);

        try
        {
            string exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Não foi possível determinar o caminho do executável.");

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = Environment.GetCommandLineString(),
                UseShellExecute = true,
                Verb = "runas",
            };

            using var process = Process.Start(psi);
            _log.LogInformation("Relançamento como administrador iniciado (UAC).");
            return Task.FromResult(process is not null);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.LogWarning("UAC recusada pelo utilizador: {Message}", ex.Message);
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha ao relançar como administrador.");
            return Task.FromResult(false);
        }
    }
}

using System.Runtime.Versioning;
using System.Security.Principal;
using HLOptimizerPro.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Verifica em runtime se a aplicação está elevada (administrador).
///
/// Mesmo com <c>requireAdministrator</c> no manifesto, o código NUNCA assume elevação:
/// há cenários reais (execução por um serviço, sessão restrita, agendador de tarefas, teste
/// unitário em CI) em que o token não é elevado. Nesses casos as operações de risco
/// devolvem <see cref="Result.Fail"/> com uma mensagem clara em vez de rebentar.
/// </summary>
public sealed class PrivilegeService
{
    private readonly ILogger<PrivilegeService> _logger;

    public PrivilegeService(ILogger<PrivilegeService> logger)
    {
        _logger = logger;
        IsElevated = DetectElevation();
        if (!IsElevated)
            _logger.LogWarning("A aplicação não está elevada — otimizações de sistema ficarão indisponíveis.");
    }

    public bool IsElevated { get; }

    public bool CanModifySystem => IsElevated;

    public string StatusText => IsElevated
        ? "Executar como administrador: ativo"
        : "Atenção: sem privilégios de administrador";

    /// <summary>Guarda a explicação usada quando uma ação é bloqueada por falta de elevação.</summary>
    public Result RequireElevation(string operationName) => IsElevated
        ? Result.Ok()
        : Result.Fail($"«{operationName}» requer privilégios de administrador. Feche a aplicação e abra-a com «Executar como administrador».");

    private static bool DetectElevation()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;

            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Identifica o utilizador atual (usado no perfil e nos relatórios). Nome amigável,
    /// sem incluir o domínio quando é o nome da máquina.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string GetCurrentUserName()
    {
        try
        {
            var name = WindowsIdentity.GetCurrent().Name;   // DOMINIO\utilizador
            var parts = name.Split('\\');
            return parts.Length > 1 ? parts[1] : name;
        }
        catch
        {
            return Environment.UserName;
        }
    }
}

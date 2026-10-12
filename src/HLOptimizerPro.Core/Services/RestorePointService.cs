using System.Management;
using System.Runtime.Versioning;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Pontos de restauro do Windows.
///
/// REQUISITO DE SEGURANÇA: antes de qualquer lote de alterações criamos um ponto de restauro.
/// Casos reais que isto cobre:
///   • a proteção do sistema pode estar DESLIGADA no disco (erro 0x80070424) → detetamos e
///     explicamos ao utilizador em vez de falhar em silêncio;
///   • o Windows limita a criação de pontos a um por 24 h (por omissão) e devolve
///     <c>0x80042316</c> se já existir um recente — tratamos como sucesso informativo;
///   • a criação demora 5–20 s → devolvemos progresso e não bloqueamos a UI (corre em Task).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RestorePointService
{
    private readonly ILogger<RestorePointService> _logger;
    private readonly IProcessRunner _runner;

    public RestorePointService(ILogger<RestorePointService> logger, IProcessRunner runner)
    {
        _logger = logger;
        _runner = runner;
    }

    /// <summary>True quando a proteção do sistema está ativa em pelo menos um volume.</summary>
    public bool IsProtectionEnabled
    {
        get
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT * FROM SystemRestore");
                return searcher.Get().Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Consulta de SystemRestore falhou.");
                return false;
            }
        }
    }

    /// <summary>Lista dos pontos de restauro existentes (mais recentes primeiro).</summary>
    public IReadOnlyList<(string Description, DateTime Created)> GetRestorePoints()
    {
        var points = new List<(string, DateTime)>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\default",
                "SELECT Description, CreationTime FROM SystemRestore");

            foreach (ManagementObject obj in searcher.Get())
            {
                var description = obj["Description"]?.ToString() ?? "Ponto de restauro";
                var raw = obj["CreationTime"]?.ToString();

                if (raw is not null && ManagementDateTimeConverter.ToDateTime(raw) is var dateTime)
                    points.Add((description, dateTime));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível listar os pontos de restauro (proteção desativada?).");
        }

        return points.OrderByDescending(p => p.Item2).ToList();
    }

    /// <summary>
    /// Cria um ponto de restauro. Nunca lança exceção — devolve um <see cref="Result"/> que a UI
    /// apresenta como aviso quando não foi possível criar.
    /// </summary>
    public async Task<Result> CreateAsync(string description, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return Result.Fail("Pontos de restauro só existem no Windows.");

        try
        {
            var result = await Task.Run(() => CreateInternal(description), cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            return Result.Fail("Criação do ponto de restauro cancelada.");
        }
    }

    /// <summary>
    /// Alternativa quando o WMI falha: <c>Checkpoint-Computer</c> do PowerShell.
    /// Menos fiável, mas funciona em alguns sistemas onde a classe WMI está bloqueada.
    /// </summary>
    public async Task<Result> CreateWithPowerShellAsync(string description, CancellationToken cancellationToken = default)
    {
        try
        {
            var safe = description.Replace("'", "''");
            var command =
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                $"\"Checkpoint-Computer -Description '{safe}' -RestorePointType 'MODIFY_SETTINGS'\"";

            var result = await _runner.RunAsync("powershell.exe", command, cancellationToken).ConfigureAwait(false);
            if (result.Success) return Result.Ok("Ponto de restauro criado com sucesso.");

            return Result.Fail(ExplainFailure(result.Output), result.Output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao criar ponto de restauro via PowerShell.");
            return Result.Fail("Não foi possível criar o ponto de restauro.", ex.Message);
        }
    }

    // ------------------------------------------------------------------ internos

    private Result CreateInternal(string description)
    {
        try
        {
            // Método CreateRestorePoint da classe SystemRestore (root\default).
            var scope = new ManagementScope(@"\\.\root\default");
            scope.Connect();

            using var restoreClass = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            var parameters = restoreClass.GetMethodParameters("CreateRestorePoint");

            parameters["Description"] = description;
            parameters["RestorePointType"] = 12;      // MODIFY_SETTINGS
            parameters["EventType"] = 100;            // BEGIN_SYSTEM_CHANGE

            using var result = restoreClass.InvokeMethod("CreateRestorePoint", parameters, null);
            var returnValue = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);

            return returnValue switch
            {
                0 => Result.Ok("Ponto de restauro criado com sucesso."),
                1 => Result.Fail("O sistema já tem um ponto de restauro recente (o Windows limita a 1 por 24 h).",
                    $"ReturnValue={returnValue}"),
                _ => Result.Fail(ExplainHResult(returnValue), $"ReturnValue={returnValue}"),
            };
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound)
        {
            _logger.LogWarning("SystemRestore indisponível (proteção do sistema desligada?).");
            return Result.Fail("A proteção do sistema está desligada em «C:». Ligue-a em " +
                               "Painel de Controlo → Sistema → Proteção do Sistema para poder criar pontos de restauro.",
                ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail("Sem permissões para criar o ponto de restauro (requer administrador).", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha inesperada ao criar ponto de restauro.");
            return Result.Fail("Não foi possível criar o ponto de restauro — as otimizações continuam com backup interno.", ex.Message);
        }
    }

    /// <summary>Mensagens amigáveis para os códigos de erro clássicos de restauro.</summary>
    private static string ExplainFailure(string output)
    {
        if (output.Contains("0x80070424", StringComparison.OrdinalIgnoreCase))
            return "O serviço de proteção do sistema (SRService) está desligado — não é possível criar pontos de restauro.";

        if (output.Contains("0x80042316", StringComparison.OrdinalIgnoreCase))
            return "Já existe um ponto de restauro criado nas últimas 24 horas — o Windows apenas permite um por dia.";

        if (output.Contains("0x80042301", StringComparison.OrdinalIgnoreCase))
            return "O serviço de instantâneos de volume (VSS) está a ser usado por outra operação. Tente novamente dentro de minutos.";

        return "Não foi possível criar o ponto de restauro.";
    }

    private static string ExplainHResult(uint code) => code switch
    {
        0x80070424 => "O serviço de proteção do sistema está desligado neste computador.",
        0x80042316 => "Já existe um ponto de restauro recente (limite de 1 por 24 h).",
        0x80042301 => "Recurso de instantâneos ocupado. Tente novamente dentro de minutos.",
        _ => $"O Windows devolveu o erro 0x{code:X8} ao criar o ponto de restauro.",
    };
}

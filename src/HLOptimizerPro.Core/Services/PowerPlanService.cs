using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Gestão de planos de energia do Windows (<c>powercfg</c>).
///
/// Notas:
///   • O plano "Desempenho Máximo" não existe em todas as instalações — só aparece depois de ser
///     desbloqueado com <c>powercfg -duplicatescheme e9a42b02-...</c>. Fazemos isso quando o
///     utilizador escolhe o perfil Extremo e mantemos o GUID criado para reverter.
///   • O plano ativo é guardado antes de qualquer alteração, para que a reversão devolva
///     exatamente o estado anterior (mesmo que o utilizador tivesse um plano personalizado).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerPlanService
{
    private static readonly Regex PlanRegex = new(
        @"Power Scheme GUID:\s*(?<guid>[0-9a-fA-F-]{36})\s*\((?<name>[^)]+)\)",
        RegexOptions.Compiled);

    private readonly ILogger<PowerPlanService> _logger;
    private readonly IProcessRunner _runner;

    public PowerPlanService(ILogger<PowerPlanService> logger, IProcessRunner runner)
    {
        _logger = logger;
        _runner = runner;
    }

    /// <summary>Todos os planos de energia disponíveis nesta máquina.</summary>
    public IReadOnlyList<PowerPlanInfo> GetPlans()
    {
        var plans = new List<PowerPlanInfo>();

        try
        {
            var result = _runner.Run("powercfg", "/list");
            if (!result.Success) return plans;

            foreach (Match match in PlanRegex.Matches(result.Output))
            {
                plans.Add(new PowerPlanInfo
                {
                    Guid = match.Groups["guid"].Value,
                    Name = match.Groups["name"].Value.Trim(),
                    IsActive = false,
                });
            }

            var active = GetActivePlanGuid();
            plans = plans.Select(p => new PowerPlanInfo
            {
                Guid = p.Guid,
                Name = p.Name,
                IsActive = string.Equals(p.Guid, active, StringComparison.OrdinalIgnoreCase),
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível listar os planos de energia.");
        }

        return plans;
    }

    public PowerPlanInfo? GetActivePlan() => GetPlans().FirstOrDefault(p => p.IsActive);

    /// <summary>GUID do plano ativo (usado para backup antes de trocar).</summary>
    public string GetActivePlanGuid()
    {
        try
        {
            var result = _runner.Run("powercfg", "/getactivescheme");
            if (!result.Success) return string.Empty;

            var match = PlanRegex.Match(result.Output);
            return match.Success ? match.Groups["guid"].Value : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Ativa um plano de energia. Devolve o GUID anterior no resultado técnico.</summary>
    public Result SetPlan(string guid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(guid)) return Result.Fail("GUID de plano de energia inválido.");

            var previous = GetActivePlanGuid();
            var result = _runner.Run("powercfg", $"/setactive {guid.Trim()}");

            if (!result.Success)
                return Result.Fail("Não foi possível ativar o plano de energia (verifique privilégios de administrador).", result.Output);

            var name = GetPlans().FirstOrDefault(p => string.Equals(p.Guid, guid, StringComparison.OrdinalIgnoreCase))?.ShortName ?? guid;
            _logger.LogInformation("Plano de energia alterado para {Plan} (anterior: {Previous}).", name, previous);

            return Result.Ok($"Plano de energia «{name}» ativado.", $"power.scheme={previous}");
        }
        catch (Exception ex)
        {
            return Result.Fail("Falha ao alterar o plano de energia.", ex.Message);
        }
    }

    /// <summary>Ativa o plano pelo nome amigável (usado pelos perfis de Tweaks).</summary>
    public Result SetPlanByName(string knownGuid, params string[] fallbackNameParts)
    {
        var plans = GetPlans();

        var target = plans.FirstOrDefault(p => string.Equals(p.Guid, knownGuid, StringComparison.OrdinalIgnoreCase));

        // O plano pode ter outro GUID nesta instalação (idioma/oem) — procura pelo nome.
        target ??= plans.FirstOrDefault(p => fallbackNameParts.Any(part =>
            p.Name.Contains(part, StringComparison.OrdinalIgnoreCase)));

        if (target is null)
            return Result.Fail($"O plano «{string.Join("/", fallbackNameParts)}» não existe nesta instalação do Windows.");

        return SetPlan(target.Guid);
    }

    /// <summary>Desbloqueia o plano "Desempenho Máximo" (Ultimate Performance) e devolve o GUID criado.</summary>
    public Result<string> UnlockUltimatePerformance()
    {
        try
        {
            var projectRoot = Path.Combine(Path.GetTempPath(), "HLOptimizerPro");
            Directory.CreateDirectory(projectRoot);

            // Duplicar o esquema cria uma nova instância; o GUID é devolvido pelo powercfg.
            var result = _runner.Run("powershell.exe",
                "-NoProfile -NonInteractive -Command \"powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61\"");

            var match = PlanRegex.Match(result.Output);
            if (!match.Success && !result.Success)
                return Result<string>.Fail("Não foi possível desbloquear o plano de Desempenho Máximo.", result.Output);

            // Depois de duplicado, procuramos pelo nome nos planos existentes.
            var created = GetPlans().FirstOrDefault(p => p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)
                                                       || p.Name.Contains("Desempenho Máximo", StringComparison.OrdinalIgnoreCase));

            return created is not null
                ? Result<string>.Ok(created.Guid, $"Plano «{created.ShortName}» disponível.")
                : Result<string>.Ok(match.Success ? match.Groups["guid"].Value : string.Empty, "Plano de Desempenho Máximo criado.");
        }
        catch (Exception ex)
        {
            return Result<string>.Fail("Falha ao desbloquear o plano de Desempenho Máximo.", ex.Message);
        }
    }

    /// <summary>Ajusta o estado do disco rígido para nunca desligar (perfis Competitivo/Extremo).</summary>
    public Result SetDiskTimeout(int seconds, bool onBattery = false)
    {
        var flag = onBattery ? "/setdcvalueindex" : "/setacvalueindex";
        var arguments = $"{flag} SCHEME_CURRENT SUB_DISK DISKIDLE {seconds}";

        var set = _runner.Run("powercfg", arguments);
        var apply = _runner.Run("powercfg", "/setactive scheme_current");

        return set.Success && apply.Success
            ? Result.Ok($"Tempo de desligar do disco definido para {(seconds == 0 ? "nunca" : $"{seconds} segundos")}.")
            : Result.Fail("Não foi possível ajustar o tempo do disco.", set.Output);
    }

    /// <summary>Ajusta o nível de desempenho mínimo/máximo do processador (0–100 %).</summary>
    public Result SetProcessorPerformance(int minimumPercent, int maximumPercent)
    {
        minimumPercent = Math.Clamp(minimumPercent, 0, 100);
        maximumPercent = Math.Clamp(maximumPercent, Math.Max(1, minimumPercent), 100);

        var operations = new[]
        {
            $"setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN {minimumPercent}",
            $"setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {maximumPercent}",
            "setactive scheme_current",
        };

        var results = operations.Select(op => _runner.Run("powercfg", op)).ToList();
        var success = results.All(r => r.Success);

        return success
            ? Result.Ok($"Processador configurado para {minimumPercent}–{maximumPercent} % de desempenho.")
            : Result.Fail("Não foi possível ajustar o desempenho do processador.", string.Join("\n", results.Select(r => r.Output)));
    }
}

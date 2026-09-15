using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Power;

/// <summary>
/// Gerenciador de planos de energia: leitura via <c>powercfg /list</c> e aplicação
/// via <c>powrprof!PowerSetActiveScheme</c> (P/Invoke instantâneo, sem processo).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que P/Invoke para ativar?</b> <c>powercfg /setactive</c> funciona, mas
/// custa ~200 ms de criação de processo. <see cref="NativeMethods.PowerSetActiveScheme"/>
/// aplica o esquema em microssegundos e não exige elevação (o esquema é uma
/// preferência do usuário). O powercfg continua sendo usado quando o P/Invoke
/// falha ou quando é preciso manipular configurações internas do esquema.
/// </para>
/// <para>
/// <b>Desempenho Máximo</b>: o GUID canônico só existe quando o plano foi
/// duplicado. Se ele não estiver listado, executamos
/// <c>powercfg -duplicatescheme e9a42b02-...</c> e ativamos o GUID resultante.
/// </para>
/// <para>
/// Os tweaks do Modo Gamer alteram índices do esquema ATUAL
/// (<c>SCHEME_CURRENT</c>) — assim o plano escolhido pelo usuário é preservado e
/// a reversão restaura os valores padrão do Windows.
/// </para>
/// </remarks>
public sealed partial class PowerPlanService : IPowerPlanService
{
    private const string PowerCfg = "powercfg.exe";

    // ---------------- GUIDs de subgrupos e configurações (documentados pela Microsoft) ----------------

    /// <summary>Subgrupo do processador.</summary>
    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";

    /// <summary>Subgrupo de configurações USB.</summary>
    private const string SubUsb = "2a737441-1930-4402-8d77-b2dba4a7b6d2";

    /// <summary>Subgrupo do disco.</summary>
    private const string SubDisk = "0012ee47-9041-4b5d-9b77-535fba8b1442";

    /// <summary>Estado mínimo do processador (PROCTHROTTLEMIN).</summary>
    private const string SettingProcessorMinimum = "893dee8e-2bef-41e0-89c6-b55d0929964c";

    /// <summary>Estado máximo do processador (PROCTHROTTLEMAX).</summary>
    private const string SettingProcessorMaximum = "bc5038f7-23e0-4960-96da-33abaf5935ec";

    /// <summary>Modo de turbo/boost do processador (PERFBOOSTMODE).</summary>
    private const string SettingBoostMode = "be337238-0d82-4146-a960-4f3749d470c7";

    /// <summary>Suspensão seletiva USB (USBSELECTSUSPEND).</summary>
    private const string SettingUsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";

    /// <summary>Tempo para desligar o disco (DISKIDLE), em segundos.</summary>
    private const string SettingDiskIdle = "6738e2c4-e8a5-4a42-b16a-e040e769756e";

    // ---------------- valores padrão do Windows (usados na reversão) ----------------

    private const int DefaultProcessorMinimumPercent = 5;
    private const int DefaultBoostMode = 1;
    private const int DefaultUsbSelectiveSuspendAc = 1;
    private const int DefaultDiskIdleSeconds = 1200;
    private const int DefaultStandbyTimeoutAcMinutes = 30;

    // ---------------- valores do Modo Gamer ----------------

    private const int GamerProcessorMinimumPercent = 100;
    private const int GamerBoostModeAggressive = 2;
    private const int GamerStandbyTimeoutMinutes = 0;

    private static readonly Regex PlanLinePattern = PlanLineRegex();
    private static readonly Regex GuidPattern = AnyGuidRegex();

    private readonly ICommandRunner _commands;
    private readonly ILogger<PowerPlanService> _logger;

    /// <summary>Cria o serviço de planos de energia.</summary>
    /// <param name="commands">Executor de comandos (powercfg).</param>
    /// <param name="logger">Logger.</param>
    public PowerPlanService(ICommandRunner commands, ILogger<PowerPlanService> logger)
    {
        _commands = commands;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _commands
                .RunAsync(PowerCfg, "/list", cancellationToken, timeout: TimeSpan.FromSeconds(20))
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("powercfg /list falhou (exit {ExitCode}): {Error}", result.ExitCode, result.StandardError);
            }

            var plans = ParsePlanList(result.StandardOutput);

            _logger.LogDebug("{Count} plano(s) de energia encontrado(s); ativo: {Active}.", plans.Count, plans.FirstOrDefault(p => p.IsActive)?.Name ?? "?");

            return plans;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha inesperada ao listar os planos de energia.");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<PowerPlanInfo?> GetActivePlanAsync(CancellationToken cancellationToken = default)
    {
        var activeGuid = GetActiveSchemeGuidFromApi();

        if (activeGuid is null)
        {
            // Fallback: powercfg /getactivescheme.
            try
            {
                var result = await _commands
                    .RunAsync(PowerCfg, "/getactivescheme", cancellationToken, timeout: TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);

                activeGuid = GuidPattern.Match(result.StandardOutput).Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível determinar o plano ativo.");
            }
        }

        if (string.IsNullOrEmpty(activeGuid))
        {
            return null;
        }

        var plans = await GetPlansAsync(cancellationToken).ConfigureAwait(false);

        return plans.FirstOrDefault(p => IsSameGuid(p.Guid, activeGuid))
            ?? new PowerPlanInfo(activeGuid, "Desconhecido", ClassifyGuid(activeGuid), true);
    }

    /// <inheritdoc />
    public async Task<bool> ActivateAsync(string planGuid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planGuid);

        if (!Guid.TryParse(planGuid, out var guid))
        {
            _logger.LogWarning("GUID de plano inválido: {Guid}.", planGuid);
            return false;
        }

        // 1) Via API nativa (rápido, sem processo).
        if (OperatingSystem.IsWindows())
        {
            var status = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref guid);

            if (status == 0)
            {
                _logger.LogInformation("Plano {Guid} ativado via PowerSetActiveScheme.", planGuid);
                return true;
            }

            _logger.LogWarning("PowerSetActiveScheme retornou 0x{Status:X}; tentando powercfg.", status);
        }

        // 2) Fallback via powercfg.
        try
        {
            var result = await _commands
                .RunAsync(PowerCfg, $"/setactive {planGuid}", cancellationToken, timeout: TimeSpan.FromSeconds(20))
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Plano {Guid} ativado via powercfg.", planGuid);
                return true;
            }

            _logger.LogError("Falha ao ativar o plano {Guid} (exit {ExitCode}): {Error}", planGuid, result.ExitCode, result.CombinedOutput);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao ativar o plano {Guid}.", planGuid);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<PowerPlanInfo?> EnableUltimatePerformanceAsync(CancellationToken cancellationToken = default)
    {
        var plans = await GetPlansAsync(cancellationToken).ConfigureAwait(false);

        var existing = plans.FirstOrDefault(p => p.Kind == PowerPlanKind.UltimatePerformance);

        if (existing is not null)
        {
            var activatedExisting = await ActivateAsync(existing.Guid, cancellationToken).ConfigureAwait(false);

            return activatedExisting ? existing with { IsActive = true } : existing;
        }

        _logger.LogInformation("Plano 'Desempenho Máximo' ausente; duplicando o esquema canônico.");

        try
        {
            var duplicate = await _commands
                .RunAsync(PowerCfg, $"-duplicatescheme {PowerPlanInfo.KnownGuids.UltimatePerformance}", cancellationToken, timeout: TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);

            if (!duplicate.IsSuccess)
            {
                _logger.LogWarning(
                    "powercfg -duplicatescheme falhou (exit {ExitCode}). Ativando Alto Desempenho como alternativa. Saída: {Output}",
                    duplicate.ExitCode,
                    duplicate.CombinedOutput);

                await ActivateAsync(PowerPlanInfo.KnownGuids.HighPerformance, cancellationToken).ConfigureAwait(false);

                return (await GetPlansAsync(cancellationToken).ConfigureAwait(false))
                    .FirstOrDefault(p => p.Kind == PowerPlanKind.HighPerformance);
            }

            var newGuid = GuidPattern.Match(duplicate.StandardOutput).Value;

            if (string.IsNullOrEmpty(newGuid))
            {
                _logger.LogWarning("powercfg duplicou o esquema mas não retornou o GUID: {Output}", duplicate.StandardOutput);
                return null;
            }

            await ActivateAsync(newGuid, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Plano 'Desempenho Máximo' criado ({Guid}) e ativado.", newGuid);

            return (await GetPlansAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(p => IsSameGuid(p.Guid, newGuid))
                ?? new PowerPlanInfo(newGuid, "Desempenho Máximo", PowerPlanKind.UltimatePerformance, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao habilitar o plano Desempenho Máximo.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RestorePlanAsync(string? planGuid, CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(planGuid) ? PowerPlanInfo.KnownGuids.Balanced : planGuid;

        var plans = await GetPlansAsync(cancellationToken).ConfigureAwait(false);

        // O plano pode ter sido removido desde que foi gravado: cai para o Equilibrado.
        if (!plans.Any(p => IsSameGuid(p.Guid, target)))
        {
            _logger.LogWarning("Plano {Guid} não existe mais; restaurando o Equilibrado.", target);
            target = PowerPlanInfo.KnownGuids.Balanced;
        }

        if (!plans.Any(p => IsSameGuid(p.Guid, target)))
        {
            // Nem o Equilibrado existe (raro): ativa o primeiro plano disponível.
            var fallback = plans.FirstOrDefault();

            if (fallback is null)
            {
                _logger.LogError("Nenhum plano de energia disponível para restaurar.");
                return false;
            }

            target = fallback.Guid;
        }

        return await ActivateAsync(target, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ApplyGamerPowerTweaksAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Aplicando tweaks de energia do Modo Gamer no esquema atual.");

        var operations = new (string Arguments, string Description)[]
        {
            ($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {SettingProcessorMinimum} {GamerProcessorMinimumPercent}", "Estado mínimo do processador (AC) = 100%"),
            ($"/setdcvalueindex SCHEME_CURRENT {SubProcessor} {SettingProcessorMinimum} {GamerProcessorMinimumPercent}", "Estado mínimo do processador (DC) = 100%"),
            ($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {SettingProcessorMaximum} 100", "Estado máximo do processador (AC) = 100%"),
            ($"/setdcvalueindex SCHEME_CURRENT {SubProcessor} {SettingProcessorMaximum} 100", "Estado máximo do processador (DC) = 100%"),
            ($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {SettingBoostMode} {GamerBoostModeAggressive}", "Boost agressivo (AC)"),
            ($"/setdcvalueindex SCHEME_CURRENT {SubProcessor} {SettingBoostMode} {GamerBoostModeAggressive}", "Boost agressivo (DC)"),
            ($"/setacvalueindex SCHEME_CURRENT {SubUsb} {SettingUsbSelectiveSuspend} 0", "Suspensão seletiva USB desativada (AC)"),
            ($"/setdcvalueindex SCHEME_CURRENT {SubUsb} {SettingUsbSelectiveSuspend} 0", "Suspensão seletiva USB desativada (DC)"),
            ($"/setacvalueindex SCHEME_CURRENT {SubDisk} {SettingDiskIdle} 0", "Desligamento de disco desativado (AC)"),
            ($"/change standby-timeout-ac {GamerStandbyTimeoutMinutes}", "Suspensão do sistema desativada na tomada"),
            ($"/change hibernate-timeout-ac {GamerStandbyTimeoutMinutes}", "Hibernação desativada na tomada")
        };

        var ok = await RunAllAsync(operations, cancellationToken).ConfigureAwait(false);

        // As mudanças de índice só valem depois de reativar o esquema corrente.
        await ApplyCurrentSchemeAsync(cancellationToken).ConfigureAwait(false);

        return ok;
    }

    /// <inheritdoc />
    public async Task<bool> RevertGamerPowerTweaksAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Revertendo tweaks de energia do Modo Gamer para os padrões do Windows.");

        var operations = new (string Arguments, string Description)[]
        {
            ($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {SettingProcessorMinimum} {DefaultProcessorMinimumPercent}", "Estado mínimo do processador (AC) = padrão"),
            ($"/setdcvalueindex SCHEME_CURRENT {SubProcessor} {SettingProcessorMinimum} {DefaultProcessorMinimumPercent}", "Estado mínimo do processador (DC) = padrão"),
            ($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {SettingProcessorMaximum} 100", "Estado máximo do processador (AC) = padrão"),
            ($"/setdcvalueindex SCHEME_CURRENT {SubProcessor} {SettingProcessorMaximum} 100", "Estado máximo do processador (DC) = padrão"),
            ($"/setacvalueindex SCHEME_CURRENT {SubProcessor} {SettingBoostMode} {DefaultBoostMode}", "Boost padrão (AC)"),
            ($"/setdcvalueindex SCHEME_CURRENT {SubProcessor} {SettingBoostMode} {DefaultBoostMode}", "Boost padrão (DC)"),
            ($"/setacvalueindex SCHEME_CURRENT {SubUsb} {SettingUsbSelectiveSuspend} {DefaultUsbSelectiveSuspendAc}", "Suspensão seletiva USB (AC) = padrão"),
            ($"/setdcvalueindex SCHEME_CURRENT {SubUsb} {SettingUsbSelectiveSuspend} 2", "Suspensão seletiva USB (DC) = padrão"),
            ($"/setacvalueindex SCHEME_CURRENT {SubDisk} {SettingDiskIdle} {DefaultDiskIdleSeconds}", "Desligamento de disco (AC) = padrão"),
            ($"/change standby-timeout-ac {DefaultStandbyTimeoutAcMinutes}", "Suspensão do sistema (AC) = padrão"),
            ($"/change hibernate-timeout-ac 0", "Hibernação (AC) = padrão")
        };

        var ok = await RunAllAsync(operations, cancellationToken).ConfigureAwait(false);

        await ApplyCurrentSchemeAsync(cancellationToken).ConfigureAwait(false);

        return ok;
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    /// <summary>Executa uma sequência de comandos powercfg, registrando cada falha.</summary>
    private async Task<bool> RunAllAsync(
        IReadOnlyList<(string Arguments, string Description)> operations,
        CancellationToken cancellationToken)
    {
        var failures = 0;

        foreach (var (arguments, description) in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await _commands
                    .RunAsync(PowerCfg, arguments, cancellationToken, timeout: TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);

                if (result.IsSuccess)
                {
                    _logger.LogDebug("powercfg {Arguments} → OK ({Description}).", arguments, description);
                }
                else
                {
                    failures++;
                    _logger.LogWarning(
                        "powercfg {Arguments} falhou (exit {ExitCode}): {Output}",
                        arguments,
                        result.ExitCode,
                        result.CombinedOutput);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;
                _logger.LogError(ex, "Exceção ao executar powercfg {Arguments}.", arguments);
            }
        }

        if (failures > 0)
        {
            _logger.LogWarning("{Failures} de {Total} ajustes de energia falharam.", failures, operations.Count);
        }

        return failures == 0;
    }

    /// <summary>Reaplica o esquema corrente para efetivar os índices alterados.</summary>
    private async Task ApplyCurrentSchemeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _commands
                .RunAsync(PowerCfg, "/setactive SCHEME_CURRENT", cancellationToken, timeout: TimeSpan.FromSeconds(20))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível reaplicar o esquema corrente.");
        }
    }

    /// <summary>Lê o GUID do esquema ativo via powrprof (sem criar processo).</summary>
    private string? GetActiveSchemeGuidFromApi()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var pointer = IntPtr.Zero;

        try
        {
            var status = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out pointer);

            if (status != 0 || pointer == IntPtr.Zero)
            {
                _logger.LogDebug("PowerGetActiveScheme retornou 0x{Status:X}.", status);
                return null;
            }

            return Marshal.PtrToStructure<Guid>(pointer).ToString("D").ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao ler o esquema ativo via powrprof.");
            return null;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                _ = NativeMethods.LocalFree(pointer);
            }
        }
    }

    /// <summary>Interpreta a saída de <c>powercfg /list</c>.</summary>
    /// <example>
    /// <code>
    /// * 381b4222-f694-41f0-9685-ff5bb260df2e  (Equilibrado)
    ///   8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Alto desempenho)
    /// </code>
    /// </example>
    private static IReadOnlyList<PowerPlanInfo> ParsePlanList(string output)
    {
        var plans = new List<PowerPlanInfo>();

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();

            var match = PlanLinePattern.Match(line);

            if (!match.Success)
            {
                continue;
            }

            var guid = match.Groups["guid"].Value.ToLowerInvariant();
            var name = match.Groups["name"].Value.Trim().Trim('(', ')').Trim();
            var isActive = match.Groups["active"].Value.Contains('*');

            plans.Add(new PowerPlanInfo(guid, string.IsNullOrEmpty(name) ? guid : name, ClassifyGuid(guid), isActive));
        }

        return plans
            .GroupBy(p => p.Guid)
            .Select(g => g.First())
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.Kind)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Classifica um GUID de plano conhecido; planos duplicados herdam o nome.</summary>
    private static PowerPlanKind ClassifyGuid(string guid)
    {
        if (IsSameGuid(guid, PowerPlanInfo.KnownGuids.PowerSaver))
        {
            return PowerPlanKind.PowerSaver;
        }

        if (IsSameGuid(guid, PowerPlanInfo.KnownGuids.Balanced))
        {
            return PowerPlanKind.Balanced;
        }

        if (IsSameGuid(guid, PowerPlanInfo.KnownGuids.HighPerformance))
        {
            return PowerPlanKind.HighPerformance;
        }

        if (IsSameGuid(guid, PowerPlanInfo.KnownGuids.UltimatePerformance))
        {
            return PowerPlanKind.UltimatePerformance;
        }

        return PowerPlanKind.Custom;
    }

    private static bool IsSameGuid(string left, string right) =>
        Guid.TryParse(left, out var a) && Guid.TryParse(right, out var b) && a == b;

    [GeneratedRegex(@"^(?<active>\*?)\s*(?<guid>[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\s*\(?\s*(?<name>[^)]*)\)?", RegexOptions.Compiled)]
    private static partial Regex PlanLineRegex();

    [GeneratedRegex("[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", RegexOptions.Compiled)]
    private static partial Regex AnyGuidRegex();
}

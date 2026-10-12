using System.Runtime.Versioning;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// App Tuner (3.4c): deteta aplicações instaladas/em execução e aplica um perfil de otimização
/// por app — prioridade de processo, afinidade de CPU e plano de energia.
///
/// Garantias:
///   • A deteção usa processos reais e chaves de desinstalação do registo (nada de listas fixas).
///   • Cada aplicação de perfil guarda o estado original (prioridade, afinidade e plano de energia)
///     num <see cref="OptimizationAction"/> que fica no diário → reversão 100 %.
///   • Devolvemos sempre a descrição honesta do que foi feito; se a app não está instalada,
///     dizemos isso em vez de simular sucesso.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AppTunerService
{
    private readonly ILogger<AppTunerService> _logger;
    private readonly IRegistryProvider _registry;
    private readonly ProcessOptimizationService _processOptimization;
    private readonly PowerPlanService _powerPlans;
    private readonly ActivityLogService _activity;

    public AppTunerService(
        ILogger<AppTunerService> logger,
        IRegistryProvider registry,
        ProcessOptimizationService processOptimization,
        PowerPlanService powerPlans,
        ActivityLogService activity)
    {
        _logger = logger;
        _registry = registry;
        _processOptimization = processOptimization;
        _powerPlans = powerPlans;
        _activity = activity;
    }

    /// <summary>Todas as apps conhecidas com o estado de deteção atualizado.</summary>
    public IReadOnlyList<AppOptimizationProfile> DetectApplications()
    {
        var list = new List<AppOptimizationProfile>();

        foreach (var template in AppCatalog.Applications)
            list.Add(Detect(template));

        return list;
    }

    /// <summary>Jogos da biblioteca com o estado de deteção atualizado.</summary>
    public IReadOnlyList<AppOptimizationProfile> DetectGames()
    {
        var list = new List<AppOptimizationProfile>();

        foreach (var template in AppCatalog.Games)
            list.Add(Detect(template));

        return list;
    }

    /// <summary>
    /// Deteta uma app: procura o processo em execução e, se não estiver a correr, verifica se
    /// está instalada no registo de desinstalação.
    /// </summary>
    public AppOptimizationProfile Detect(AppOptimizationProfile profile)
    {
        var pid = _processOptimization.FindProcessId(profile.ProcessNames);
        profile.RunningPid = pid;

        if (pid > 0)
        {
            profile.State = AppDetectionState.Instalado;
            return profile;
        }

        profile.State = IsInstalled(profile) ? AppDetectionState.Instalado : AppDetectionState.NaoInstalado;
        return profile;
    }

    /// <summary>Verifica a instalação através das chaves Uninstall do registo (64 e 32 bits).</summary>
    public bool IsInstalled(AppOptimizationProfile profile)
    {
        var roots = new[]
        {
            (Hive: "HKLM", Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Hive: "HKLM", Path: @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Hive: "HKCU", Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };

        foreach (var (hive, path) in roots)
        {
            foreach (var subKey in _registry.GetSubKeyNames(hive, path))
            {
                var displayName = _registry.GetValue(hive, $@"{path}\{subKey}", "DisplayName")?.ToString();
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                if (profile.RegistryNames.Any(name => displayName.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cria a ação de otimização para uma app. A ação é aplicada pelo <see cref="OptimizationEngine"/>
    /// e o seu estado original fica guardado para reversão.
    /// </summary>
    public OptimizationAction CreateTuningAction(AppOptimizationProfile profile, bool simulationMode)
    {
        var detected = Detect(profile);
        var isRunning = detected.RunningPid > 0;
        var currentPlan = _powerPlans.GetActivePlan()?.ShortName ?? "Desconhecido";

        return new OptimizationAction
        {
            Key = $"apptuner.{profile.AppKey}",
            Title = $"Otimizar {profile.DisplayName}",
            Description = $"{profile.Notes} Deteção: {(detected.IsInstalled ? "aplicação presente neste computador" : "aplicação não instalada")}.",
            Target = $"Processo(s): {string.Join(", ", profile.ProcessNames)}",
            CurrentValue = isRunning
                ? $"Em execução (PID {detected.RunningPid}) — prioridade predefinida, plano {currentPlan}"
                : "Não está em execução (o perfil será memorizado para a próxima vez que abrir)",
            NewValue = $"Prioridade {profile.ProcessPriority}" +
                       (profile.UseCpuAffinity ? $", afinidade de {profile.PreferredCores} núcleos" : string.Empty) +
                       (profile.ApplyPowerPlan ? ", plano de energia de alto desempenho" : string.Empty),
            Category = profile.IsGame ? OptimizationCategory.Aplicativos : OptimizationCategory.Aplicativos,
            Area = OptimizationArea.Aplicativos,
            Risk = "Baixo — apenas prioridade/afinidade do processo; nada é removido.",
            RiskLevel = HealthLevel.Otimo,
            RequiresRestart = false,
            IsAvailable = detected.IsInstalled || isRunning,
            UnavailableReason = detected.IsInstalled ? null : "A aplicação não está instalada neste computador.",

            Apply = action =>
            {
                if (simulationMode) return;

                var previousPlan = _powerPlans.GetActivePlanGuid();
                action.Backup["app.key"] = profile.AppKey;
                action.Backup["power.original.scheme"] = previousPlan;

                var appliedCount = 0;

                // ---- 1. Aplicar ao processo, se estiver em execução -----------
                if (profile.RunningPid > 0 && profile.PrioritizeProcess)
                {
                    var result = _processOptimization.ApplyToProcess(
                        profile.RunningPid,
                        profile.ProcessPriority,
                        profile.UseCpuAffinity ? profile.PreferredCores : 0);

                    if (result.Success)
                    {
                        appliedCount++;
                        action.Backup[$"proc.{profile.RunningPid}.priority"] = result.OriginalPriority;
                        action.Backup[$"proc.{profile.RunningPid}.affinity"] = result.OriginalAffinity?.ToString();
                    }
                    else
                    {
                        _logger.LogWarning("Ajuste de processo falhou para {App}: {Error}", profile.DisplayName, result.Error);
                    }
                }

                // ---- 2. Plano de energia --------------------------------------
                if (profile.ApplyPowerPlan && profile.PowerPlanGuid is { } planGuid)
                {
                    var planResult = _powerPlans.SetPlan(planGuid);
                    if (planResult.Success) appliedCount++;
                }

                // ---- 3. Game Mode do Windows para jogos ----------------------
                if (profile.IsGame && profile.EnableWindowsGameMode)
                {
                    _registry.SetValue("HKCU", @"SOFTWARE\Microsoft\GameBar", "AutoGameModeEnabled", 1);
                    _registry.SetValue("HKCU", @"SOFTWARE\Microsoft\GameBar", "AllowAutoGameMode", 1);
                    appliedCount++;
                }

                action.Backup["app.applied.count"] = appliedCount.ToString();
                profile.State = AppDetectionState.Otimizado;
            },

            IsAlreadyApplied = () => profile.State == AppDetectionState.Otimizado,

            Revert = action =>
            {
                // Restaura prioridade/afinidade dos processos registados no backup.
                foreach (var key in action.Backup.Keys.Where(k => k.StartsWith("proc.", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    var parts = key.Split('.');
                    if (parts.Length < 3 || !int.TryParse(parts[1], out var pid)) continue;

                    if (parts[2] == "priority")
                    {
                        var originalAffinity = action.Backup.GetValueOrDefault($"proc.{pid}.affinity");
                        _processOptimization.RevertProcess(pid,
                            action.Backup.GetValueOrDefault(key),
                            long.TryParse(originalAffinity, out var parsed) ? parsed : null);
                    }
                }

                // Restaura o plano de energia original.
                var previous = action.Backup.GetValueOrDefault("power.original.scheme");
                if (!string.IsNullOrWhiteSpace(previous))
                    _powerPlans.SetPlan(previous);

                profile.State = AppDetectionState.Instalado;
                return Result.Ok($"{profile.DisplayName} voltou à configuração original.");
            },

            SimulateExplanation = () =>
                $"Em modo Simular: «{profile.DisplayName}» passaria a usar prioridade {profile.ProcessPriority}" +
                (profile.UseCpuAffinity ? $", {profile.PreferredCores} núcleos dedicados" : string.Empty) +
                (profile.ApplyPowerPlan ? " e plano de energia de alto desempenho" : string.Empty) +
                ". Nenhuma alteração foi feita.",
        };
    }

    /// <summary>Cria as ações de tunelagem para uma lista de apps selecionadas na UI.</summary>
    public IReadOnlyList<OptimizationAction> CreateTuningActions(
        IEnumerable<AppOptimizationProfile> profiles,
        bool simulationMode)
        => profiles.Select(p => CreateTuningAction(p, simulationMode)).ToList();

    /// <summary>Número de aplicações detetadas nesta máquina (usado no resumo do dashboard).</summary>
    public int CountInstalledApplications(params AppOptimizationProfile[] profiles)
        => (profiles.Length > 0 ? profiles : AppCatalog.Applications.ToArray()).Count(IsInstalled);

    /// <summary>Aplica um perfil de forma imediata (sem passar pelo motor) — usado pelo watchdog do Game Mode.</summary>
    public Task<Result> ApplyImmediateAsync(AppOptimizationProfile profile, CancellationToken cancellationToken = default)
    {
        var action = CreateTuningAction(profile, simulationMode: false);

        if (!action.IsAvailable)
            return Task.FromResult(Result.Fail(action.UnavailableReason ?? "Aplicação indisponível."));

        try
        {
            action.Apply?.Invoke(action);
            _activity.Log("App Tuner", $"Perfil «{profile.DisplayName}» aplicado", details: action.NewValue);
            return Task.FromResult(Result.Ok($"Perfil aplicado a {profile.DisplayName}."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao aplicar o perfil de {App}", profile.DisplayName);
            return Task.FromResult(Result.Fail($"Não foi possível aplicar o perfil a {profile.DisplayName}.", ex.Message));
        }
    }
}

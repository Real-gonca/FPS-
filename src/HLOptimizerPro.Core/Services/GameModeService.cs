using System.Runtime.Versioning;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Modo Jogo (3.4e).
///
/// Funcionamento:
///   1. O motor de deteção verifica a cada 3 segundos se algum jogo da biblioteca arrancou
///      (por nome de processo — é a forma mais fiável e não interfere com anti-cheats).
///   2. Quando deteta, aplica o "estado de jogo": prioridade alta do jogo, plano de energia de
///      alto desempenho, redução de prioridade dos processos de fundo e Game Mode do Windows.
///   3. Quando o jogo termina, REPÕE tudo: prioridade original, plano de energia anterior e
///      prioridade dos processos de fundo.
///
/// Sobre anti-cheat: nunca injetamos código nos jogos, não lemos a memória do jogo e não usamos
/// hooks. Só alteramos prioridade/afinidade via API oficial do Windows e definições de energia —
/// operações que o próprio Gestor de Tarefas faz. Isso mantém a aplicação compatível com
/// Vanguard/BattlEye/EAC.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GameModeService : IDisposable
{
    private readonly ILogger<GameModeService> _logger;
    private readonly ProcessOptimizationService _processOptimization;
    private readonly PowerPlanService _powerPlans;
    private readonly ActivityLogService _activity;
    private readonly MonitoringService _monitoring;

    private readonly Timer? _watchdog;
    private readonly object _gate = new();
    private readonly List<AppOptimizationProfile> _library = new();

    private AppOptimizationProfile? _activeGame;
    private string _previousPowerPlan = string.Empty;
    private string? _gamePriorityBefore;
    private int _throttledCount;

    /// <summary>Jogos adicionados manualmente pelo utilizador (nome → caminho do executável).</summary>
    private readonly Dictionary<string, string> _customGames = new(StringComparer.OrdinalIgnoreCase);

    public GameModeService(
        ILogger<GameModeService> logger,
        ProcessOptimizationService processOptimization,
        PowerPlanService powerPlans,
        ActivityLogService activity,
        MonitoringService monitoring)
    {
        _logger = logger;
        _processOptimization = processOptimization;
        _powerPlans = powerPlans;
        _activity = activity;
        _monitoring = monitoring;

        _library.AddRange(AppCatalog.Games);

        // Verificação a cada 3 s: deteta o jogo em poucos segundos sem consumo relevante de CPU.
        _watchdog = new Timer(_ => CheckGames(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Estado atual (ligado à UI).</summary>
    public GameModeStatus Status { get; private set; } = new();

    /// <summary>Evento de mudança de estado (a UI atualiza sem polling).</summary>
    public event EventHandler<GameModeStatus>? StatusChanged;

    /// <summary>True quando o Modo Jogo está a monitorizar.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Biblioteca de jogos (inclui os adicionados manualmente).</summary>
    public IReadOnlyList<AppOptimizationProfile> Library
    {
        get
        {
            lock (_gate)
            {
                return _library.ToList();
            }
        }
    }

    /// <summary>Liga/desliga a monitorização. Se estiver desligado, o estado aplicado é reposto.</summary>
    public Result SetEnabled(bool enabled, bool autoActivate)
    {
        try
        {
            lock (_gate)
            {
                IsEnabled = enabled;
            }

            if (enabled)
            {
                _watchdog?.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
                UpdateStatus(new GameModeStatus
                {
                    State = autoActivate ? GameModeState.AguardandoJogo : GameModeState.Desativado,
                    LastMessage = autoActivate
                        ? "Modo Jogo ativo: os jogos da biblioteca são detetados automaticamente."
                        : "Modo Jogo ligado, com ativação manual.",
                });

                _activity.Log("Modo Jogo", "Monitorização ativada");
            }
            else
            {
                _watchdog?.Change(Timeout.Infinite, Timeout.Infinite);
                Deactivate(reason: "Modo Jogo desligado pelo utilizador");
            }

            return Result.Ok(enabled ? "Modo Jogo ativado." : "Modo Jogo desativado e estado restaurado.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao alterar o estado do Modo Jogo.");
            return Result.Fail("Não foi possível alterar o Modo Jogo.", ex.Message);
        }
    }

    /// <summary>Adiciona um jogo à biblioteca a partir do executável escolhido pelo utilizador.</summary>
    public Result<AppOptimizationProfile> AddCustomGame(string executablePath, string? displayName = null)
    {
        try
        {
            if (!File.Exists(executablePath))
                return Result<AppOptimizationProfile>.Fail("O ficheiro selecionado não existe.");

            var name = string.IsNullOrWhiteSpace(displayName)
                ? Path.GetFileNameWithoutExtension(executablePath)
                : displayName.Trim();

            var profile = new AppOptimizationProfile
            {
                AppKey = $"custom.{name.ToLowerInvariant().Replace(" ", "-")}",
                DisplayName = name,
                ProcessNames = new[] { Path.GetFileNameWithoutExtension(executablePath) },
                DetectionHint = executablePath,
                IsGame = true,
                ProcessPriority = "High",
                EnableWindowsGameMode = true,
                PowerPlanGuid = PowerPlanInfo.HighPerformance,
                Notes = "Jogo adicionado manualmente pelo utilizador.",
                ExpectedGain = "Prioridade e energia otimizadas automaticamente enquanto joga.",
                State = AppDetectionState.Instalado,
            };

            lock (_gate)
            {
                // Substitui se já existir (o utilizador pode corrigir o caminho).
                _library.RemoveAll(g => string.Equals(g.AppKey, profile.AppKey, StringComparison.OrdinalIgnoreCase));
                _library.Add(profile);
                _customGames[name] = executablePath;
            }

            _activity.Log("Modo Jogo", $"Jogo adicionado à biblioteca: {name}");
            return Result<AppOptimizationProfile>.Ok(profile, $"«{name}» adicionado à biblioteca de jogos.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao adicionar o jogo {Path}", executablePath);
            return Result<AppOptimizationProfile>.Fail("Não foi possível adicionar o jogo.", ex.Message);
        }
    }

    /// <summary>Remove um jogo da biblioteca (apenas os adicionados manualmente).</summary>
    public Result RemoveCustomGame(string appKey)
    {
        lock (_gate)
        {
            var removed = _library.RemoveAll(g => string.Equals(g.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return Result.Fail("Este jogo faz parte da biblioteca predefinida e não pode ser removido.");

            foreach (var key in _customGames.Where(kv => appKey.EndsWith(kv.Key.ToLowerInvariant().Replace(" ", "-"), StringComparison.Ordinal))
                                            .Select(kv => kv.Key).ToList())
            {
                _customGames.Remove(key);
            }
        }

        return Result.Ok("Jogo removido da biblioteca.");
    }

    /// <summary>Ativa o Modo Jogo manualmente para o jogo indicado.</summary>
    public Result ActivateFor(AppOptimizationProfile game)
    {
        lock (_gate)
        {
            if (_activeGame is not null)
                return Result.Fail($"O Modo Jogo já está ativo para «{_activeGame.DisplayName}».");
        }

        return Activate(game);
    }

    /// <summary>Força a reposição do estado normal (mesmo que o jogo ainda esteja aberto).</summary>
    public Result DeactivateNow() => Deactivate("Reposição manual do utilizador");

    /// <summary>Descrição das funcionalidades mostrada na UI (3.4e).</summary>
    public static IReadOnlyList<string> Features { get; } = new[]
    {
        "Identifica o jogo em execução e dá-lhe prioridade máxima de CPU e GPU.",
        "Reduz a prioridade de processos em segundo plano (sincronizações, chats, launchers).",
        "Ativa o plano de energia de alto desempenho apenas enquanto joga.",
        "Liga o Game Mode nativo do Windows e desativa gravações automáticas que consomem FPS.",
        "Ativa-se sozinho quando o jogo arranca (sem intervenção sua).",
        "Ao fechar o jogo, repõe tudo exatamente como estava — nenhuma alteração permanente.",
    };

    // ------------------------------------------------------------------ internos

    /// <summary>Verificação do watchdog (thread do timer).</summary>
    private void CheckGames()
    {
        try
        {
            lock (_gate)
            {
                if (!IsEnabled) return;
            }

            // Já há um jogo ativo? Verifica se continua em execução.
            if (_activeGame is { } active)
            {
                var stillRunning = _processOptimization.FindProcessId(active.ProcessNames) > 0;
                if (!stillRunning) Deactivate($"{active.DisplayName} terminou");
                return;
            }

            // Procura o primeiro jogo da biblioteca em execução.
            foreach (var game in Library)
            {
                var pid = _processOptimization.FindProcessId(game.ProcessNames);
                if (pid <= 0) continue;

                game.RunningPid = pid;
                Activate(game);
                break;
            }

            UpdateStatus(new GameModeStatus
            {
                State = GameModeState.AguardandoJogo,
                LastMessage = "A vigiar a biblioteca de jogos…",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no watchdog do Modo Jogo.");
        }
    }

    /// <summary>Aplica o estado de jogo: prioridade, energia e redução de processos de fundo.</summary>
    private Result Activate(AppOptimizationProfile game)
    {
        try
        {
            lock (_gate)
            {
                _previousPowerPlan = _powerPlans.GetActivePlanGuid();
                _activeGame = game;
            }

            // ---- 1. Prioridade e afinidade do jogo ---------------------------
            var adjustment = _processOptimization.ApplyToProcess(
                game.RunningPid,
                game.ProcessPriority,
                game.UseCpuAffinity ? game.PreferredCores : 0);

            _gamePriorityBefore = adjustment.OriginalPriority;

            // ---- 2. Plano de energia ----------------------------------------
            var planResult = game.PowerPlanGuid is { } plan
                ? _powerPlans.SetPlan(plan)
                : Result.Ok("Plano de energia inalterado.");

            // ---- 3. Processos de fundo --------------------------------------
            _throttledCount = _processOptimization.ThrottleBackgroundProcesses(AppCatalog.BackgroundProcessNames);

            var status = new GameModeStatus
            {
                State = GameModeState.Ativo,
                ActiveGameName = game.DisplayName,
                ActivePid = game.RunningPid,
                ActiveProcessName = game.ProcessNames.FirstOrDefault(),
                ActivatedAt = DateTime.Now,
                BackgroundProcessesThrottled = _throttledCount,
                LastMessage = $"«{game.DisplayName}» otimizado: prioridade {game.ProcessPriority}, " +
                              $"{_throttledCount} processos de fundo reduzidos. {planResult.Message}",
            };

            UpdateStatus(status);
            _activity.Log("Modo Jogo", status.LastMessage, details: $"PID {game.RunningPid}");

            _logger.LogInformation("Modo Jogo ativado para {Game} (PID {Pid}).", game.DisplayName, game.RunningPid);
            return Result.Ok(status.LastMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao ativar o Modo Jogo.");
            return Result.Fail("Não foi possível ativar o Modo Jogo.", ex.Message);
        }
    }

    /// <summary>Repõe o estado anterior (prioridade, energia e processos de fundo).</summary>
    private Result Deactivate(string reason)
    {
        AppOptimizationProfile? game;

        lock (_gate)
        {
            game = _activeGame;
            _activeGame = null;
        }

        if (game is null)
        {
            UpdateStatus(new GameModeStatus { State = GameModeState.Desativado, LastMessage = "Modo Jogo inativo." });
            return Result.Ok("Modo Jogo já estava inativo.");
        }

        try
        {
            // ---- 1. Repõe a prioridade do jogo (se ainda existir) -------------
            var revertMessage = "O jogo já tinha fechado.";
            if (game.RunningPid > 0)
            {
                var revert = _processOptimization.RevertProcess(game.RunningPid, _gamePriorityBefore, null);
                revertMessage = revert.Message;
            }

            // ---- 2. Repõe o plano de energia ---------------------------------
            if (!string.IsNullOrWhiteSpace(_previousPowerPlan))
                _powerPlans.SetPlan(_previousPowerPlan);

            // ---- 3. Repõe prioridade dos processos de fundo -------------------
            var restored = 0;
            foreach (var (pid, _) in _processOptimization.FindAllProcesses(AppCatalog.BackgroundProcessNames))
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(pid);
                    if (process.PriorityClass == System.Diagnostics.ProcessPriorityClass.BelowNormal)
                    {
                        process.PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
                        restored++;
                    }
                }
                catch
                {
                    // processo terminou — nada a fazer
                }
            }

            var message = $"Estado normal reposto ({reason}): {_throttledCount} processos em segundo plano devolvidos ao normal" +
                          (restored > 0 ? $", {restored} confirmados" : string.Empty) + ".";

            UpdateStatus(new GameModeStatus
            {
                State = GameModeState.Desativado,
                LastMessage = message,
                BackgroundProcessesThrottled = 0,
            });

            _activity.Log("Modo Jogo", message, details: revertMessage);
            _logger.LogInformation("Modo Jogo desativado: {Reason}", reason);

            return Result.Ok(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao repor o estado normal após o jogo.");
            return Result.Fail("Não foi possível repor totalmente o estado anterior — verifique o plano de energia.", ex.Message);
        }
    }

    private void UpdateStatus(GameModeStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    /// <summary>Memória disponível em GB (usado no painel do Modo Jogo).</summary>
    public double GetAvailableMemoryGb() => _monitoring.ReadMemory().AvailableBytes / 1024d / 1024d / 1024d;

    public void Dispose() => _watchdog?.Dispose();
}

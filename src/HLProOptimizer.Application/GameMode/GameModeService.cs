using System.Diagnostics;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode;

/// <summary>
/// Orquestrador do Modo Gamer / FPS Boost.
/// </summary>
/// <remarks>
/// A ativação aplica os tweaks na ordem do catálogo (energia primeiro, processos
/// por último) e persiste o estado - incluindo o plano de energia anterior e os
/// valores de registro originais - para que a desativação reverta TUDO, sem
/// deixar resíduos no sistema.
/// </remarks>
public sealed class GameModeService : IGameModeService
{
    private readonly IReadOnlyList<IGameModeTweak> _tweaks;
    private readonly ISystemInformationService _systemInformation;
    private readonly ISettingsService _settingsService;
    private readonly IGameModeRepository _repository;
    private readonly IPowerPlanService _powerPlanService;
    private readonly IProcessService _processService;
    private readonly IActionHistoryService _history;
    private readonly ILogger<GameModeService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GameModeState _state = GameModeState.Inactive;

    /// <summary>Cria o serviço do Modo Gamer.</summary>
    public GameModeService(
        IEnumerable<IGameModeTweak> tweaks,
        ISystemInformationService systemInformation,
        ISettingsService settingsService,
        IGameModeRepository repository,
        IPowerPlanService powerPlanService,
        IProcessService processService,
        IActionHistoryService history,
        ILogger<GameModeService> logger)
    {
        _tweaks = tweaks.ToList();
        _systemInformation = systemInformation;
        _settingsService = settingsService;
        _repository = repository;
        _powerPlanService = powerPlanService;
        _processService = processService;
        _history = history;
        _logger = logger;

        logger.LogInformation("GameModeService inicializado com {Count} tweak(s).", _tweaks.Count);
    }

    /// <inheritdoc />
    public event EventHandler<GameModeState>? StateChanged;

    /// <inheritdoc />
    public GameModeState CurrentState => _state;

    /// <inheritdoc />
    public bool IsActive => _state.IsActive;

    /// <inheritdoc />
    public IReadOnlyList<GameTweak> AvailableTweaks
    {
        get
        {
            var tweaks = GameTweakCatalog.CreateTweaks();

            foreach (var tweak in tweaks)
            {
                var applied = _state.AppliedTweaks.FirstOrDefault(t =>
                    string.Equals(t.Id, tweak.Id, StringComparison.OrdinalIgnoreCase));

                tweak.WasApplied = applied is not null;
                tweak.LastResult = applied?.LastResult ?? string.Empty;
            }

            return tweaks;
        }
    }

    /// <inheritdoc />
    public async Task<GameModeState> ActivateAsync(
        IReadOnlyCollection<string>? enabledTweakIds = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_state.IsActive)
            {
                _logger.LogInformation("Modo Gamer já está ativo; nada a fazer.");
                progress?.Report("O Modo Gamer já está ativo.");
                return _state;
            }

            var stopwatch = Stopwatch.StartNew();
            var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var profile = await _systemInformation.GetSystemProfileAsync(cancellationToken).ConfigureAwait(false);
            var isElevated = _systemInformation.IsAdministrator;

            var context = new GameModeContext(settings, profile, isElevated);

            // Registra o plano de energia antes de qualquer alteração.
            var previousPlan = await _powerPlanService.GetActivePlanAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report("Preparando o Modo Gamer...");

            var ordered = _tweaks
                .OrderBy(t => GameTweakCatalog.Find(t.Id)?.Order ?? int.MaxValue)
                .ToList();

            var applied = new List<GameTweak>();
            var failed = new List<GameTweak>();

            foreach (var tweak in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var definition = GameTweakCatalog.Find(tweak.Id);
                var isEnabled = enabledTweakIds is null
                    || enabledTweakIds.Contains(tweak.Id, StringComparer.OrdinalIgnoreCase);

                if (!isEnabled)
                {
                    progress?.Report($"⏭ {definition?.DisplayName ?? tweak.Id}: desativado pelo usuário.");
                    continue;
                }

                if (definition?.RequiresAdmin == true && !isElevated)
                {
                    failed.Add(NewTweak(definition, tweak.Id, false, "Requer administrador."));
                    progress?.Report($"⛔ {definition?.DisplayName ?? tweak.Id}: requer administrador.");
                    continue;
                }

                progress?.Report($"→ Aplicando {definition?.DisplayName ?? tweak.Id}...");

                try
                {
                    var success = await tweak.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
                    var instance = NewTweak(definition, tweak.Id, success, success ? "Aplicado." : "Não aplicado.");

                    if (success)
                    {
                        applied.Add(instance);
                        progress?.Report($"✔ {definition?.DisplayName ?? tweak.Id} aplicado.");
                    }
                    else
                    {
                        failed.Add(instance);
                        progress?.Report($"⚠ {definition?.DisplayName ?? tweak.Id} não pôde ser aplicado.");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Falha ao aplicar o tweak {TweakId} do Modo Gamer.", tweak.Id);
                    failed.Add(NewTweak(definition, tweak.Id, false, ex.Message));
                    progress?.Report($"✖ {definition?.DisplayName ?? tweak.Id}: {ex.Message}");
                }
            }

            stopwatch.Stop();

            _state = new GameModeState
            {
                IsActive = true,
                ActivatedAt = DateTime.Now,
                AppliedTweaks = applied,
                FailedTweaks = failed,
                TerminatedProcesses = [.. context.TerminatedProcesses],
                FreedMemoryBytes = context.FreedMemoryBytes,
                PreviousPowerPlanGuid = previousPlan?.Guid
            };

            await PersistStateAsync(_state, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Modo Gamer ativado em {Elapsed}: {Applied} tweak(s) aplicado(s), {Failed} falha(s), {Memory} liberados.",
                stopwatch.Elapsed, applied.Count, failed.Count, context.FreedMemoryBytes);

            await _history.RecordAsync(
                ActionKind.GameMode,
                "Modo Gamer ativado",
                $"{applied.Count} ajuste(s) aplicado(s), {failed.Count} falha(s), {context.TerminatedProcesses.Count} processo(s) encerrado(s).",
                context.FreedMemoryBytes,
                stopwatch.ElapsedMilliseconds,
                failed.Count == 0,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            RaiseStateChanged();

            return _state;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GameModeState> DeactivateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_state.IsActive)
            {
                _logger.LogInformation("Modo Gamer já está inativo.");
                return _state;
            }

            var stopwatch = Stopwatch.StartNew();
            var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var profile = await _systemInformation.GetSystemProfileAsync(cancellationToken).ConfigureAwait(false);
            var context = new GameModeContext(settings, profile, _systemInformation.IsAdministrator);

            // A reversão usa os valores padrão do Windows gravados em cada tweak;
            // o único estado que precisa ser persistido entre ativação e desativação
            // é o plano de energia anterior (já guardado em GameModeState).
            var reverted = 0;
            var failedRevert = 0;

            // Reverte na ordem inversa da aplicação.
            foreach (var appliedTweak in _state.AppliedTweaks.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tweak = _tweaks.FirstOrDefault(t =>
                    string.Equals(t.Id, appliedTweak.Id, StringComparison.OrdinalIgnoreCase));

                if (tweak is null)
                {
                    continue;
                }

                try
                {
                    if (await tweak.RevertAsync(context, cancellationToken).ConfigureAwait(false))
                    {
                        reverted++;
                    }
                    else
                    {
                        failedRevert++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedRevert++;
                    _logger.LogError(ex, "Falha ao reverter o tweak {TweakId} do Modo Gamer.", appliedTweak.Id);
                }
            }

            // Garante que o plano de energia anterior seja restaurado.
            await _powerPlanService.RestorePlanAsync(_state.PreviousPowerPlanGuid, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            _state = GameModeState.Inactive;

            await _repository.ClearAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Modo Gamer desativado em {Elapsed}: {Reverted} tweak(s) revertido(s), {Failed} falha(s).",
                stopwatch.Elapsed, reverted, failedRevert);

            await _history.RecordAsync(
                ActionKind.GameMode,
                "Modo Gamer desativado",
                $"{reverted} ajuste(s) revertido(s), {failedRevert} falha(s).",
                success: failedRevert == 0,
                durationMs: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            RaiseStateChanged();

            return _state;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<GameModeState> RestoreStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var persisted = await _repository.LoadStateAsync(cancellationToken).ConfigureAwait(false);

            if (persisted is not { IsActive: true })
            {
                return _state;
            }

            _logger.LogInformation("Estado persistido do Modo Gamer encontrado; reativando.");

            var tweakIds = persisted.AppliedTweaks.Select(t => t.Id).ToList();

            return await ActivateAsync(tweakIds, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao restaurar o estado do Modo Gamer.");
            return _state;
        }
    }

    /// <inheritdoc />
    public async Task<bool> BoostGameProcessAsync(string processName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var boosted = await _processService.BoostAsync(processName, cancellationToken).ConfigureAwait(false);

        if (boosted)
        {
            _logger.LogInformation("Prioridade elevada para o jogo '{Game}'.", processName);
        }

        return boosted;
    }

    /// <summary>Cria uma instância de <see cref="GameTweak"/> a partir do catálogo.</summary>
    private static GameTweak NewTweak(GameTweakDefinition? definition, string id, bool applied, string result)
    {
        return new GameTweak
        {
            Id = id,
            DisplayName = definition?.DisplayName ?? id,
            Description = definition?.Description ?? string.Empty,
            IsEnabled = true,
            RequiresAdmin = definition?.RequiresAdmin ?? false,
            WasApplied = applied,
            LastResult = result,
            EstimatedFpsGain = definition?.EstimatedFpsGain ?? 0,
            Order = definition?.Order ?? int.MaxValue
        };
    }

    /// <summary>Persiste o estado (melhor esforço).</summary>
    private async Task PersistStateAsync(GameModeState state, CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Não foi possível persistir o estado do Modo Gamer.");
        }
    }

    /// <summary>Dispara o evento de mudança de estado.</summary>
    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke(this, _state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Um assinante de StateChanged lançou exceção.");
        }
    }
}

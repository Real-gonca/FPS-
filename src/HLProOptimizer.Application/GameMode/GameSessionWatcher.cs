using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode;

/// <summary>
/// Observador de sessões de jogo baseado em polling leve de processos.
/// </summary>
/// <remarks>
/// O polling (5s) é deliberadamente simples e barato: enumerar processos custa
/// poucos milissegundos e evita a complexidade/fragilidade de hooks globais ou
/// de eventos WMI (Win32_ProcessStartTrace exige elevação). Quando o jogo
/// encerra e <c>GameModeAutoDisableOnGameExit</c> está ativo, o Modo Gamer é
/// desativado automaticamente.
/// </remarks>
public sealed class GameSessionWatcher : IGameSessionWatcher, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IProcessService _processService;
    private readonly ISettingsService _settingsService;
    private readonly IGameModeService _gameModeService;
    private readonly ILogger<GameSessionWatcher> _logger;
    private readonly object _sync = new();
    private Timer? _timer;
    private string? _currentGame;
    private int _pollInProgress;
    private bool _disposed;

    /// <summary>Cria o observador.</summary>
    public GameSessionWatcher(
        IProcessService processService,
        ISettingsService settingsService,
        IGameModeService gameModeService,
        ILogger<GameSessionWatcher> logger)
    {
        _processService = processService;
        _settingsService = settingsService;
        _gameModeService = gameModeService;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<string>? GameStarted;

    /// <inheritdoc />
    public event EventHandler<string>? GameStopped;

    /// <inheritdoc />
    public bool IsWatching => _timer is not null;

    /// <inheritdoc />
    public string? CurrentGame => _currentGame;

    /// <inheritdoc />
    public void Start()
    {
        lock (_sync)
        {
            if (_disposed || _timer is not null)
            {
                return;
            }

            _timer = new Timer(_ => _ = PollAsync(), null, TimeSpan.FromSeconds(3), PollInterval);
            _logger.LogInformation("Observador de sessões de jogo iniciado (intervalo de {Interval}s).", PollInterval.TotalSeconds);
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
            _currentGame = null;
        }
    }

    /// <summary>Executa um ciclo de detecção.</summary>
    private async Task PollAsync()
    {
        // Evita reentrância caso um ciclo demore mais que o intervalo.
        if (Interlocked.Exchange(ref _pollInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            var settings = _settingsService.Current;
            var candidates = settings.GameProcessNames;

            if (candidates.Count == 0)
            {
                return;
            }

            var running = await _processService.FindRunningAsync(candidates, CancellationToken.None).ConfigureAwait(false);

            if (running is not null && _currentGame is null)
            {
                _currentGame = running;
                _logger.LogInformation("Jogo detectado: {Game}.", running);
                GameStarted?.Invoke(this, running);

                if (_gameModeService.IsActive)
                {
                    await _gameModeService.BoostGameProcessAsync(running, CancellationToken.None).ConfigureAwait(false);
                }
            }
            else if (running is null && _currentGame is not null)
            {
                var finished = _currentGame;
                _currentGame = null;

                _logger.LogInformation("Jogo encerrado: {Game}.", finished);
                GameStopped?.Invoke(this, finished);

                if (settings.GameModeAutoDisableOnGameExit && _gameModeService.IsActive)
                {
                    _logger.LogInformation("Desativando o Modo Gamer automaticamente após o fim do jogo.");
                    await _gameModeService.DeactivateAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha em um ciclo do observador de jogos.");
        }
        finally
        {
            Interlocked.Exchange(ref _pollInProgress, 0);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>
/// Detecta um jogo em execução (pela lista configurada ou por heurística de
/// janela em tela cheia) e eleva sua prioridade de CPU para Alta.
/// </summary>
public sealed class GamePriorityTweak : IGameModeTweak
{
    private readonly IProcessService _processService;
    private readonly ILogger<GamePriorityTweak> _logger;

    /// <summary>Cria o tweak.</summary>
    /// <param name="processService">Serviço de processos.</param>
    /// <param name="logger">Logger.</param>
    public GamePriorityTweak(IProcessService processService, ILogger<GamePriorityTweak> logger)
    {
        _processService = processService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => GameTweakCatalog.Ids.GamePriority;

    /// <inheritdoc />
    public async Task<bool> ApplyAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var candidates = context.Settings.GameProcessNames;

        if (candidates.Count == 0)
        {
            context.Log($"[{Id}] nenhum jogo configurado - cadastre os executáveis em Configurações.");
            return false;
        }

        var running = await _processService.FindRunningAsync(candidates, cancellationToken).ConfigureAwait(false);

        if (running is null)
        {
            context.Log($"[{Id}] nenhum jogo em execução no momento.");
            return false;
        }

        _logger.LogInformation("Jogo detectado em execução: {Game}.", running);

        var boosted = await _processService.BoostAsync(running, cancellationToken).ConfigureAwait(false);
        context.Log($"[{Id}] prioridade do jogo '{running}' {(boosted ? "elevada para Alta" : "não pôde ser alterada")}.");

        return boosted;
    }

    /// <inheritdoc />
    public Task<bool> RevertAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        // A prioridade volta ao normal quando o processo é encerrado.
        return Task.FromResult(true);
    }
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>Libera RAM compactando o working set dos processos restantes.</summary>
public sealed class MemoryTweak : IGameModeTweak
{
    private readonly IProcessService _processService;
    private readonly ILogger<MemoryTweak> _logger;

    /// <summary>Cria o tweak.</summary>
    /// <param name="processService">Serviço de processos.</param>
    /// <param name="logger">Logger.</param>
    public MemoryTweak(IProcessService processService, ILogger<MemoryTweak> logger)
    {
        _processService = processService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => GameTweakCatalog.Ids.Memory;

    /// <inheritdoc />
    public async Task<bool> ApplyAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var freed = await _processService.CompactBackgroundMemoryAsync(cancellationToken).ConfigureAwait(false);
        context.FreedMemoryBytes += freed;
        context.Log($"[{Id}] {ByteFormat.Format(freed)} de RAM liberados.");

        _logger.LogDebug("Modo Gamer liberou {Bytes} bytes de RAM.", freed);

        return freed > 0;
    }

    /// <inheritdoc />
    public Task<bool> RevertAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        // A memória liberada volta a ser usada naturalmente pelo sistema.
        return Task.FromResult(true);
    }
}

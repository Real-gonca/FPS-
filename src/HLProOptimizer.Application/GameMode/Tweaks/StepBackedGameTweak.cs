using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>
/// Tweak que delega a um <see cref="IOptimizationStep"/> já existente (DRY):
/// o Modo Gamer reaproveita exatamente a mesma lógica (e a mesma reversão) usada
/// pela tela de Otimização, sem duplicar código de registro/serviços.
/// </summary>
public abstract class StepBackedGameTweak : IGameModeTweak
{
    private readonly IOptimizationStep? _step;
    private readonly ILogger _logger;

    /// <summary>Cria o tweak a partir do conjunto de passos registrados.</summary>
    /// <param name="steps">Passos de otimização disponíveis.</param>
    /// <param name="stepId">Id do passo que implementa o ajuste.</param>
    /// <param name="logger">Logger.</param>
    protected StepBackedGameTweak(IEnumerable<IOptimizationStep> steps, string stepId, ILogger logger)
    {
        _step = steps.FirstOrDefault(s => string.Equals(s.Id, stepId, StringComparison.OrdinalIgnoreCase));
        _logger = logger;

        if (_step is null)
        {
            logger.LogWarning("Passo de otimização '{StepId}' não encontrado para o tweak do Modo Gamer.", stepId);
        }
    }

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <summary>Passo delegado (pode ser nulo se não estiver registrado).</summary>
    protected IOptimizationStep? Step => _step;

    /// <inheritdoc />
    public virtual async Task<bool> ApplyAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_step is null)
        {
            return false;
        }

        var result = await _step.ExecuteAsync(context.Optimization, cancellationToken).ConfigureAwait(false);
        context.Log($"[{Id}] {result.Message}");

        return result.Success && !result.Skipped;
    }

    /// <inheritdoc />
    public virtual async Task<bool> RevertAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_step is null || !_step.IsReversible)
        {
            _logger.LogDebug("Tweak {TweakId} não é reversível.", Id);
            return true;
        }

        var result = await _step.RevertAsync(context.Optimization, cancellationToken).ConfigureAwait(false);
        context.Log($"[{Id}] reversão: {result.Message}");

        return result.Success;
    }
}

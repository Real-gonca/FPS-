using System.Diagnostics;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Exceptions;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization;

/// <summary>
/// Classe base dos passos de otimização (Template Method).
/// </summary>
/// <remarks>
/// Centraliza cronometragem, logging estruturado, isolamento de exceções e o
/// contrato de resultado, para que cada passo implemente apenas a sua lógica em
/// <see cref="ExecuteCoreAsync"/>. Isso mantém os passos curtos, uniformes e
/// seguros: nenhuma exceção de um passo derruba a otimização inteira.
/// </remarks>
public abstract class OptimizationStepBase : IOptimizationStep
{
    /// <summary>Cria o passo com o logger da categoria concreta.</summary>
    /// <param name="logger">Logger.</param>
    protected OptimizationStepBase(ILogger logger)
    {
        Logger = logger;
    }

    /// <summary>Logger compartilhado pelas implementações.</summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract IReadOnlyCollection<OptimizationMode> SupportedModes { get; }

    /// <inheritdoc />
    public virtual OptimizationLevel MinimumLevel => OptimizationLevel.Conservative;

    /// <inheritdoc />
    public virtual int Order => 100;

    /// <inheritdoc />
    public virtual bool RequiresAdmin => false;

    /// <inheritdoc />
    public virtual bool IsReversible => false;

    /// <inheritdoc />
    public async Task<OptimizationStepResult> ExecuteAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();
        context.Log($"→ {Name}");

        try
        {
            var (success, message, bytesFreed) = await ExecuteCoreAsync(context, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            context.AddFreedBytes(bytesFreed);
            context.Log(success ? $"✔ {Name}: {message}" : $"⚠ {Name}: {message}");

            Logger.LogInformation("Passo {StepId} concluído em {Elapsed}ms (sucesso={Success}, bytes={Bytes}).",
                Id, stopwatch.ElapsedMilliseconds, success, bytesFreed);

            return new OptimizationStepResult(Id, Name, success, message, bytesFreed, false, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            context.Log($"⏹ {Name}: cancelado pelo usuário.");
            throw;
        }
        catch (ElevationRequiredException ex)
        {
            stopwatch.Stop();
            context.Log($"⛔ {Name}: requer administrador ({ex.Operation}).");

            return new OptimizationStepResult(Id, Name, false, ex.Message, 0, true, stopwatch.Elapsed, ex.Message);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            context.Log($"✖ {Name}: falha - {ex.Message}");
            Logger.LogError(ex, "Falha ao executar o passo de otimização {StepId}.", Id);

            return new OptimizationStepResult(Id, Name, false, $"Falha: {ex.Message}", 0, false, stopwatch.Elapsed, ex.ToString());
        }
    }

    /// <inheritdoc />
    public virtual Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsReversible)
        {
            context.Log($"↩ {Name}: passo não reversível, ignorado.");

            return Task.FromResult(new OptimizationStepResult(Id, Name, true, "Passo não reversível.", 0, true, TimeSpan.Zero));
        }

        return Task.FromResult(new OptimizationStepResult(Id, Name, true, "Reversão não implementada.", 0, true, TimeSpan.Zero));
    }

    /// <summary>Lógica concreta do passo.</summary>
    /// <param name="context">Contexto compartilhado da otimização.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Tupla (sucesso, mensagem exibida no log, bytes liberados).</returns>
    protected abstract Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken);
}

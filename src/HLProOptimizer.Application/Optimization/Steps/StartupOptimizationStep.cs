using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>Desativa programas de inicialização não essenciais para acelerar o boot.</summary>
public sealed class StartupOptimizationStep : OptimizationStepBase
{
    private readonly IStartupManager _startupManager;

    /// <summary>Cria o passo.</summary>
    /// <param name="startupManager">Gerenciador de inicialização.</param>
    /// <param name="logger">Logger.</param>
    public StartupOptimizationStep(IStartupManager startupManager, ILogger<StartupOptimizationStep> logger)
        : base(logger)
    {
        _startupManager = startupManager;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.Startup;

    /// <inheritdoc />
    public override string Name => "Otimizar inicialização";

    /// <inheritdoc />
    public override string Description => "Desativa itens de boot não essenciais e remove entradas órfãs.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Full,
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override OptimizationLevel MinimumLevel => OptimizationLevel.Balanced;

    /// <inheritdoc />
    public override int Order => 100;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.OptimizeStartup)
        {
            return (true, "Desativado nas opções.", 0);
        }

        var programs = await _startupManager.GetStartupProgramsAsync(cancellationToken).ConfigureAwait(false);

        if (programs.Count == 0)
        {
            return (true, "Nenhum programa de inicialização encontrado.", 0);
        }

        var summaryBefore = _startupManager.Summarize(programs);
        var disabled = await _startupManager.DisableNonEssentialAsync(programs, cancellationToken).ConfigureAwait(false);

        if (disabled.Count == 0)
        {
            return (true, "Nenhum item de inicialização precisou ser desativado.", 0);
        }

        var updated = await _startupManager.GetStartupProgramsAsync(cancellationToken).ConfigureAwait(false);
        var summaryAfter = _startupManager.Summarize(updated);

        return (
            true,
            $"{disabled.Count} item(ns) desativado(s). Boot estimado: {summaryBefore.CurrentBootSeconds:F1}s → {summaryAfter.CurrentBootSeconds:F1}s.",
            0);
    }
}

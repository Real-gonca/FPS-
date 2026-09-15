using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Reinicia o Windows Explorer para aplicar imediatamente os ajustes de interface
/// (efeitos visuais, taskbar). Só é executado quando o usuário habilita a opção.
/// </summary>
public sealed class RestartExplorerStep : OptimizationStepBase
{
    private readonly ICommandRunner _commandRunner;

    /// <summary>Cria o passo.</summary>
    /// <param name="commandRunner">Executor de comandos externos.</param>
    /// <param name="logger">Logger.</param>
    public RestartExplorerStep(ICommandRunner commandRunner, ILogger<RestartExplorerStep> logger)
        : base(logger)
    {
        _commandRunner = commandRunner;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.RestartExplorer;

    /// <inheritdoc />
    public override string Name => "Reiniciar o Explorer";

    /// <inheritdoc />
    public override string Description => "Reinicia o Windows Explorer para aplicar os ajustes de interface imediatamente.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Full,
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override OptimizationLevel MinimumLevel => OptimizationLevel.Aggressive;

    /// <inheritdoc />
    public override int Order => 900;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.RestartExplorer)
        {
            return (true, "Desativado nas opções.", 0);
        }

        var kill = await _commandRunner.RunAsync(
            "taskkill.exe",
            "/F /IM explorer.exe",
            cancellationToken).ConfigureAwait(false);

        // Pequena pausa para o shell liberar handles antes de reiniciar.
        await Task.Delay(750, cancellationToken).ConfigureAwait(false);

        var start = await _commandRunner.RunHiddenAsync(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            string.Empty,
            cancellationToken).ConfigureAwait(false);

        var success = kill.IsSuccess || start == 0;

        return success
            ? (true, "Windows Explorer reiniciado.", 0)
            : (false, $"Falha ao reiniciar o Explorer (taskkill={kill.ExitCode}, start={start}).", 0);
    }
}

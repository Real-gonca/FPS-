using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Cria um ponto de restauração do Windows antes de qualquer alteração.
/// É sempre o primeiro passo (Order negativo) e pode ser desligado pelo usuário.
/// </summary>
public sealed class RestorePointStep : OptimizationStepBase
{
    private readonly IBackupService _backupService;

    /// <summary>Cria o passo.</summary>
    /// <param name="backupService">Serviço de backup/pontos de restauração.</param>
    /// <param name="logger">Logger.</param>
    public RestorePointStep(IBackupService backupService, ILogger<RestorePointStep> logger)
        : base(logger)
    {
        _backupService = backupService;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.RestorePoint;

    /// <inheritdoc />
    public override string Name => "Criar ponto de restauração";

    /// <inheritdoc />
    public override string Description => "Gera um ponto de restauração do Windows para permitir reverter as alterações.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Quick,
        OptimizationMode.Full,
        OptimizationMode.Gamer,
        OptimizationMode.Privacy
    ];

    /// <inheritdoc />
    public override int Order => -100;

    /// <inheritdoc />
    public override bool RequiresAdmin => true;

    /// <inheritdoc />
    public override bool IsReversible => false;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.CreateRestorePoint)
        {
            return (true, "Desativado nas opções.", 0);
        }

        if (!context.Settings.CreateRestorePointBeforeChanges)
        {
            return (true, "Desativado nas configurações.", 0);
        }

        var description = $"HL PRO OPTIMIZER - {context.Options.Mode} - {DateTime.Now:dd/MM/yyyy HH:mm}";

        var point = await _backupService.CreateRestorePointAsync(description, cancellationToken).ConfigureAwait(false);

        if (point is null)
        {
            return (false, "Não foi possível criar o ponto de restauração (Proteção do Sistema pode estar desativada).", 0);
        }

        context.RestorePointCreated = true;
        context.RestorePointDescription = point.Description;

        return (true, $"Ponto de restauração '{point.Description}' criado.", 0);
    }
}

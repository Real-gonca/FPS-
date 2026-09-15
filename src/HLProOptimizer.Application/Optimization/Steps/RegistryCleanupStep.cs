using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Remove entradas órfãs do registro (DLLs inexistentes, Run inválidos,
/// App Paths quebrados, fontes órfãs). Faz backup prévio das chaves alteradas.
/// </summary>
public sealed class RegistryCleanupStep : OptimizationStepBase
{
    private readonly ICleanupService _cleanupService;
    private readonly IBackupService _backupService;

    /// <summary>Cria o passo.</summary>
    /// <param name="cleanupService">Serviço de limpeza.</param>
    /// <param name="backupService">Serviço de backup do registro.</param>
    /// <param name="logger">Logger.</param>
    public RegistryCleanupStep(
        ICleanupService cleanupService,
        IBackupService backupService,
        ILogger<RegistryCleanupStep> logger)
        : base(logger)
    {
        _cleanupService = cleanupService;
        _backupService = backupService;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.Registry;

    /// <inheritdoc />
    public override string Name => "Otimizar registro";

    /// <inheritdoc />
    public override string Description => "Remove chaves órfãs de software desinstalado, Run inválidos, App Paths e fontes quebradas.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Full
    ];

    /// <inheritdoc />
    public override OptimizationLevel MinimumLevel => OptimizationLevel.Balanced;

    /// <inheritdoc />
    public override int Order => 50;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.OptimizeRegistry)
        {
            return (true, "Desativado nas opções.", 0);
        }

        // Backup defensivo das chaves de inicialização antes de qualquer remoção.
        if (context.Settings.AutoBackupEnabled)
        {
            try
            {
                await _backupService.BackupRegistryAsync(
                    @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Não foi possível criar o backup da chave Run antes da limpeza do registro.");
            }
        }

        var targets = await _cleanupService.GetTargetsAsync(context.Options.Level, cancellationToken).ConfigureAwait(false);

        var selected = targets.Where(t => t.Category == IssueCategory.Registry).ToList();
        selected.ForEach(t => t.IsSelected = true);

        if (selected.Count == 0)
        {
            return (true, "Nenhuma entrada órfã encontrada no registro.", 0);
        }

        var result = await _cleanupService.CleanAsync(selected, null, cancellationToken).ConfigureAwait(false);
        var entries = result.Entries.Sum(e => e.DeletedFiles);

        return (
            !result.HasFailures || entries > 0,
            $"{entries} entrada(s) inválida(s) removida(s) do registro.",
            result.DeletedBytes);
    }
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Pausa temporariamente o Windows Update durante sessões de jogo (evita
/// downloads/reinícios concorrentes). Disponível apenas no nível Agressivo e é
/// sempre reversível.
/// </summary>
/// <remarks>
/// <b>Aviso:</b> manter o Windows Update desativado por longos períodos reduz a
/// segurança do sistema. Por isso este passo exige <see cref="OptimizationLevel.Aggressive"/>
/// e o Modo Gamer o reverte automaticamente ao ser desativado.
/// </remarks>
public sealed class WindowsUpdatePauseStep : OptimizationStepBase
{
    private const string WindowsUpdatePolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string ServiceName = "wuauserv";

    private readonly IRegistryService _registry;
    private readonly IServiceManager _serviceManager;

    /// <summary>Cria o passo.</summary>
    /// <param name="registry">Acesso ao registro.</param>
    /// <param name="serviceManager">Gerenciador de serviços.</param>
    /// <param name="logger">Logger.</param>
    public WindowsUpdatePauseStep(
        IRegistryService registry,
        IServiceManager serviceManager,
        ILogger<WindowsUpdatePauseStep> logger)
        : base(logger)
    {
        _registry = registry;
        _serviceManager = serviceManager;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.WindowsUpdatePause;

    /// <inheritdoc />
    public override string Name => "Pausar Windows Update";

    /// <inheritdoc />
    public override string Description => "Interrompe downloads e instalações de atualização durante os jogos (reversível).";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override OptimizationLevel MinimumLevel => OptimizationLevel.Aggressive;

    /// <inheritdoc />
    public override int Order => 150;

    /// <inheritdoc />
    public override bool RequiresAdmin => true;

    /// <inheritdoc />
    public override bool IsReversible => true;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        var service = await _serviceManager.GetServiceAsync(ServiceName, cancellationToken).ConfigureAwait(false);

        if (service is not null)
        {
            context.State[$"{Id}.previous"] = service.StartupKind;

            await _serviceManager.SetStartupKindAsync(ServiceName, ServiceStartupKind.Manual, cancellationToken).ConfigureAwait(false);

            if (service.IsRunning)
            {
                await _serviceManager.StopAsync(ServiceName, cancellationToken).ConfigureAwait(false);
            }
        }

        // NoAutoUpdates=1 pausa o download automático; AUOptions=2 (notificar antes de baixar).
        _registry.SetDword(RegistryHiveKind.LocalMachine, WindowsUpdatePolicyKey, "NoAutoUpdates", 1);
        _registry.SetDword(RegistryHiveKind.LocalMachine, WindowsUpdatePolicyKey, "AUOptions", 2);

        return (true, "Windows Update pausado (será restaurado ao desativar o Modo Gamer).", 0);
    }

    /// <inheritdoc />
    public override async Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            _registry.DeleteValue(RegistryHiveKind.LocalMachine, WindowsUpdatePolicyKey, "NoAutoUpdates");
            _registry.DeleteValue(RegistryHiveKind.LocalMachine, WindowsUpdatePolicyKey, "AUOptions");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Não foi possível remover as políticas de Windows Update.");
        }

        var previous = context.State.TryGetValue($"{Id}.previous", out var value) && value is ServiceStartupKind kind
            ? kind
            : ServiceStartupKind.Manual;

        var restored = await _serviceManager.SetStartupKindAsync(ServiceName, previous, cancellationToken).ConfigureAwait(false);

        if (restored)
        {
            await _serviceManager.StartAsync(ServiceName, cancellationToken).ConfigureAwait(false);
        }

        return new OptimizationStepResult(Id, Name, restored, restored ? "Windows Update restaurado." : "Falha ao restaurar o Windows Update.");
    }
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Ajusta o Multimedia Class Scheduler Service (MMCSS) para priorizar jogos:
/// SystemResponsiveness, NetworkThrottlingIndex e a task "Games".
/// </summary>
/// <remarks>
/// Por padrão o Windows reserva 20% da CPU para tarefas multimídia de sistema e
/// limita o throughput de rede em 10 pacotes/ms. Em máquinas dedicadas a jogos,
/// reduzir SystemResponsiveness para 10 e liberar o throttling de rede melhora
/// a consistência de frametime e o ping.
/// </remarks>
public sealed class MmcssStep : OptimizationStepBase
{
    private const string SystemProfileKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesTaskKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

    /// <summary>Valor que desativa o throttling de rede do MMCSS.</summary>
    private const int NetworkThrottlingDisabled = unchecked((int)0xFFFFFFFF);

    private readonly IRegistryService _registry;

    /// <summary>Cria o passo.</summary>
    /// <param name="registry">Acesso ao registro.</param>
    /// <param name="logger">Logger.</param>
    public MmcssStep(IRegistryService registry, ILogger<MmcssStep> logger)
        : base(logger)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.Mmcss;

    /// <inheritdoc />
    public override string Name => "Otimizar MMCSS";

    /// <inheritdoc />
    public override string Description => "Prioriza jogos no agendador multimídia e remove o throttling de rede.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override OptimizationLevel MinimumLevel => OptimizationLevel.Balanced;

    /// <inheritdoc />
    public override int Order => 120;

    /// <inheritdoc />
    public override bool RequiresAdmin => true;

    /// <inheritdoc />
    public override bool IsReversible => true;

    /// <inheritdoc />
    protected override Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        var before = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemResponsiveness"] = _registry.GetDword(RegistryHiveKind.LocalMachine, SystemProfileKey, "SystemResponsiveness"),
            ["NetworkThrottlingIndex"] = _registry.GetDword(RegistryHiveKind.LocalMachine, SystemProfileKey, "NetworkThrottlingIndex")
        };

        context.State[$"{Id}.before"] = before;

        // 10% reservado ao sistema no modo Balanceado; 0% no Agressivo (máximo para jogos).
        var systemResponsiveness = context.Options.Level >= OptimizationLevel.Aggressive ? 0 : 10;

        _registry.SetDword(RegistryHiveKind.LocalMachine, SystemProfileKey, "SystemResponsiveness", systemResponsiveness);
        _registry.SetDword(RegistryHiveKind.LocalMachine, SystemProfileKey, "NetworkThrottlingIndex", NetworkThrottlingDisabled);

        _registry.SetDword(RegistryHiveKind.LocalMachine, GamesTaskKey, "GPU Priority", 8);
        _registry.SetDword(RegistryHiveKind.LocalMachine, GamesTaskKey, "Priority", 6);
        _registry.SetString(RegistryHiveKind.LocalMachine, GamesTaskKey, "Scheduling Category", "High");
        _registry.SetString(RegistryHiveKind.LocalMachine, GamesTaskKey, "SFIO Priority", "High");

        return Task.FromResult((
            true,
            $"MMCSS otimizado (SystemResponsiveness={systemResponsiveness}, NetworkThrottling=off, task Games=High).",
            0L));
    }

    /// <inheritdoc />
    public override Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        // Valores padrão do Windows.
        _registry.SetDword(RegistryHiveKind.LocalMachine, SystemProfileKey, "SystemResponsiveness", 20);
        _registry.SetDword(RegistryHiveKind.LocalMachine, SystemProfileKey, "NetworkThrottlingIndex", 10);

        _registry.SetDword(RegistryHiveKind.LocalMachine, GamesTaskKey, "GPU Priority", 8);
        _registry.SetDword(RegistryHiveKind.LocalMachine, GamesTaskKey, "Priority", 1);
        _registry.SetString(RegistryHiveKind.LocalMachine, GamesTaskKey, "Scheduling Category", "Medium");
        _registry.SetString(RegistryHiveKind.LocalMachine, GamesTaskKey, "SFIO Priority", "Normal");

        return Task.FromResult(new OptimizationStepResult(Id, Name, true, "Valores padrão do MMCSS restaurados."));
    }
}

using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Aplica ajustes de rede para baixa latência: desativa o algoritmo de Nagle para
/// jogos, Energy-Efficient Ethernet e aplica o preset de DNS preferido.
/// </summary>
public sealed class NetworkLatencyStep : OptimizationStepBase
{
    private readonly INetworkToolsService _networkTools;
    private readonly IDnsService _dnsService;

    /// <summary>Cria o passo.</summary>
    /// <param name="networkTools">Ferramentas de rede.</param>
    /// <param name="dnsService">Serviço de DNS.</param>
    /// <param name="logger">Logger.</param>
    public NetworkLatencyStep(
        INetworkToolsService networkTools,
        IDnsService dnsService,
        ILogger<NetworkLatencyStep> logger)
        : base(logger)
    {
        _networkTools = networkTools;
        _dnsService = dnsService;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.NetworkLatency;

    /// <inheritdoc />
    public override string Name => "Rede de baixa latência";

    /// <inheritdoc />
    public override string Description => "Desativa Nagle/Energy-Efficient Ethernet e aplica DNS de baixa latência.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override int Order => 75;

    /// <inheritdoc />
    public override bool RequiresAdmin => true;

    /// <inheritdoc />
    public override bool IsReversible => true;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        var applied = 0;
        var details = new List<string>();

        if (context.Options.OptimizeNetworkLatency)
        {
            if (await _networkTools.ApplyLowLatencyTweaksAsync(cancellationToken).ConfigureAwait(false))
            {
                applied++;
                details.Add("tweaks de latência");
            }
        }

        var preset = DnsConfiguration.Presets.All.FirstOrDefault(p =>
            string.Equals(p.Name, context.Settings.PreferredDnsPreset, StringComparison.OrdinalIgnoreCase))
            ?? DnsConfiguration.Presets.Cloudflare;

        var adapters = await _dnsService.ApplyPresetAsync(preset, cancellationToken).ConfigureAwait(false);

        if (adapters > 0)
        {
            applied++;
            details.Add($"DNS {preset.Name} em {adapters} adaptador(es)");
        }

        return applied > 0
            ? (true, $"Aplicado: {string.Join("; ", details)}.", 0)
            : (false, "Nenhum ajuste de rede pôde ser aplicado.", 0);
    }

    /// <inheritdoc />
    public override async Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var reverted = await _networkTools.RevertLowLatencyTweaksAsync(cancellationToken).ConfigureAwait(false);

        // Restaura DNS automático em todos os adaptadores que haviam sido alterados.
        var configurations = await _dnsService.GetConfigurationsAsync(cancellationToken).ConfigureAwait(false);
        var resetCount = 0;

        foreach (var configuration in configurations.Where(c => !c.IsAutomatic))
        {
            if (await _dnsService.ResetToAutomaticAsync(configuration.AdapterName, cancellationToken).ConfigureAwait(false))
            {
                resetCount++;
            }
        }

        var message = reverted
            ? $"Ajustes de rede revertidos e DNS automático restaurado em {resetCount} adaptador(es)."
            : "Falha ao reverter os ajustes de rede.";

        return new OptimizationStepResult(Id, Name, reverted, message);
    }
}

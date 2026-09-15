using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>Executa o flush do cache DNS (resolve lentidão de resolução e falhas de conexão).</summary>
public sealed class DnsFlushStep : OptimizationStepBase
{
    private readonly IDnsService _dnsService;

    /// <summary>Cria o passo.</summary>
    /// <param name="dnsService">Serviço de DNS.</param>
    /// <param name="logger">Logger.</param>
    public DnsFlushStep(IDnsService dnsService, ILogger<DnsFlushStep> logger)
        : base(logger)
    {
        _dnsService = dnsService;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.DnsFlush;

    /// <inheritdoc />
    public override string Name => "Flush de DNS";

    /// <inheritdoc />
    public override string Description => "Limpa o cache de resolução DNS (ipconfig /flushdns).";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Quick,
        OptimizationMode.Full,
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override int Order => 70;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.FlushDns)
        {
            return (true, "Desativado nas opções.", 0);
        }

        var result = await _dnsService.FlushCacheAsync(cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? (true, "Cache DNS limpo com sucesso.", 0)
            : (false, $"Falha ao limpar o cache DNS (código {result.ExitCode}).", 0);
    }
}

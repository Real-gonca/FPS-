using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>Esvazia a Lixeira (opcional - desligado por padrão nos modos conservadores).</summary>
public sealed class RecycleBinStep : OptimizationStepBase
{
    private readonly IFileSystemService _fileSystem;

    /// <summary>Cria o passo.</summary>
    /// <param name="fileSystem">Serviço de arquivos.</param>
    /// <param name="logger">Logger.</param>
    public RecycleBinStep(IFileSystemService fileSystem, ILogger<RecycleBinStep> logger)
        : base(logger)
    {
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.RecycleBin;

    /// <inheritdoc />
    public override string Name => "Esvaziar a Lixeira";

    /// <inheritdoc />
    public override string Description => "Remove definitivamente os itens da Lixeira.";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Full,
        OptimizationMode.Quick
    ];

    /// <inheritdoc />
    public override int Order => 40;

    /// <inheritdoc />
    protected override async Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Options.EmptyRecycleBin)
        {
            return (true, "Desativado nas opções.", 0);
        }

        var sizeBefore = await _fileSystem.GetRecycleBinSizeAsync(cancellationToken).ConfigureAwait(false);

        if (sizeBefore <= 0)
        {
            return (true, "A Lixeira já estava vazia.", 0);
        }

        var freed = await _fileSystem.EmptyRecycleBinAsync(cancellationToken).ConfigureAwait(false);

        // Alguns sistemas não reportam o tamanho liberado; usa a medição anterior.
        var bytes = freed > 0 ? freed : sizeBefore;

        return (true, $"Lixeira esvaziada ({ByteFormat.Format(bytes)}).", bytes);
    }
}

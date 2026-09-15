using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Elevation.Operations;

/// <summary>Ativa/desativa o Modo Gamer em processo elevado.</summary>
public sealed class GameModeElevatedOperation : IElevatedOperation
{
    private readonly IGameModeService _gameModeService;

    /// <summary>Cria a operação.</summary>
    /// <param name="gameModeService">Serviço do Modo Gamer.</param>
    public GameModeElevatedOperation(IGameModeService gameModeService)
    {
        _gameModeService = gameModeService;
    }

    /// <inheritdoc />
    public string Id => ElevatedOperationIds.GameMode;

    /// <inheritdoc />
    public string Description => "Aplicar o Modo Gamer como administrador";

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string payloadJson, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var request = JsonConvert.DeserializeObject<GameModeRequest>(payloadJson ?? string.Empty) ?? new GameModeRequest();

        var reporter = new Progress<string>(message => progress?.Report(message));

        var state = request.Activate
            ? await _gameModeService.ActivateAsync(request.TweakIds, reporter, cancellationToken).ConfigureAwait(false)
            : await _gameModeService.DeactivateAsync(cancellationToken).ConfigureAwait(false);

        return JsonConvert.SerializeObject(state);
    }

    /// <summary>Requisição da operação.</summary>
    public sealed class GameModeRequest
    {
        /// <summary>True para ativar, false para desativar.</summary>
        public bool Activate { get; set; } = true;

        /// <summary>Ids dos tweaks a aplicar (null = todos).</summary>
        public List<string>? TweakIds { get; set; }
    }
}

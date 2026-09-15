using HLProOptimizer.Core.Abstractions;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Elevation.Operations;

/// <summary>Executa um comando externo arbitrário com privilégios de administrador.</summary>
public sealed class CommandElevatedOperation : IElevatedOperation
{
    private readonly ICommandRunner _commandRunner;

    /// <summary>Cria a operação.</summary>
    /// <param name="commandRunner">Executor de comandos.</param>
    public CommandElevatedOperation(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    /// <inheritdoc />
    public string Id => ElevatedOperationIds.Command;

    /// <inheritdoc />
    public string Description => "Executar comando do Windows como administrador";

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string payloadJson, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var request = JsonConvert.DeserializeObject<CommandRequest>(payloadJson ?? string.Empty) ?? new CommandRequest();

        progress?.Report($"Executando (elevado): {request.FileName} {request.Arguments}");

        var result = await _commandRunner.RunAsync(
            request.FileName,
            request.Arguments ?? string.Empty,
            cancellationToken,
            elevated: false,
            timeout: request.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(request.TimeoutSeconds) : null,
            onOutputLine: line => progress?.Report(line)).ConfigureAwait(false);

        return JsonConvert.SerializeObject(result);
    }

    /// <summary>Requisição da operação.</summary>
    public sealed class CommandRequest
    {
        /// <summary>Executável.</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>Argumentos.</summary>
        public string? Arguments { get; set; }

        /// <summary>Timeout em segundos (0 = padrão).</summary>
        public int TimeoutSeconds { get; set; }
    }
}

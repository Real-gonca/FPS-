using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Elevação sob demanda. O aplicativo usa manifest <c>asInvoker</c>; quando uma
/// operação exige administrador, este serviço executa a ação em um processo
/// elevado (ou re-executa o próprio aplicativo elevado, se o usuário preferir).
/// </summary>
public interface IElevationService
{
    /// <summary>Indica se o processo atual já está elevado.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Executa um comando em um processo elevado, aguardando sua conclusão.
    /// </summary>
    /// <param name="fileName">Executável.</param>
    /// <param name="arguments">Argumentos.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CommandResult> RunElevatedAsync(string fileName, string arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executa uma ação local em um processo elevado auxiliar do próprio aplicativo
    /// (<c>HLProOptimizer.exe --elevated-run &lt;operationId&gt;</c>), usado para
    /// operações que envolvem múltiplas chamadas de API.
    /// </summary>
    /// <param name="operationId">Identificador da operação registrada no host elevado.</param>
    /// <param name="payloadJson">Parâmetros serializados em JSON.</param>
    /// <param name="progress">Canal opcional para receber o log em tempo real do processo elevado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado textual/JSON produzido pela operação elevada.</returns>
    Task<string> RunElevatedOperationAsync(
        string operationId,
        string payloadJson,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Re-executa o aplicativo inteiro com privilégios de administrador.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>True quando o novo processo elevado foi iniciado.</returns>
    Task<bool> RestartElevatedAsync(CancellationToken cancellationToken = default);

    /// <summary>Explica ao usuário por que a elevação é necessária (texto localizado).</summary>
    /// <param name="operation">Operação solicitada.</param>
    string GetReason(string operation);
}

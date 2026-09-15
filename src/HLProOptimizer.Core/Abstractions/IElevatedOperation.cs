namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Operação executável em um processo elevado auxiliar.
/// </summary>
/// <remarks>
/// Como o aplicativo usa manifest <c>asInvoker</c>, ações que exigem administrador
/// são delegadas a uma segunda instância do próprio executável iniciada com
/// <c>--elevated-run &lt;operationId&gt; &lt;payloadFile&gt; &lt;resultFile&gt; &lt;progressFile&gt;</c>.
/// O contrato é intencionalmente simples (JSON entra, JSON sai) para que qualquer
/// serviço possa ser "elevado" sem acoplamento com WPF.
/// </remarks>
public interface IElevatedOperation
{
    /// <summary>Identificador da operação (usado na linha de comando).</summary>
    string Id { get; }

    /// <summary>Descrição curta (usada no prompt de UAC).</summary>
    string Description { get; }

    /// <summary>Executa a operação.</summary>
    /// <param name="payloadJson">Parâmetros serializados em JSON (pode ser vazio).</param>
    /// <param name="progress">Canal de progresso/log em tempo real.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado serializado em JSON.</returns>
    Task<string> ExecuteAsync(string payloadJson, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Host que executa operações elevadas no processo auxiliar.
/// É invocado pelo <c>App</c> quando o executável inicia com <c>--elevated-run</c>.
/// </summary>
public interface IElevatedOperationHost
{
    /// <summary>Ids das operações suportadas.</summary>
    IReadOnlyList<string> SupportedOperations { get; }

    /// <summary>
    /// Executa uma operação elevada lendo o payload de um arquivo e gravando o
    /// resultado (e o progresso) em arquivos, para leitura pelo processo chamador.
    /// </summary>
    /// <param name="operationId">Identificador da operação.</param>
    /// <param name="payloadFile">Arquivo JSON com os parâmetros.</param>
    /// <param name="resultFile">Arquivo onde o resultado JSON será gravado.</param>
    /// <param name="progressFile">Arquivo de progresso (append), opcional.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Código de saída do processo (0 = sucesso).</returns>
    Task<int> RunAsync(
        string operationId,
        string payloadFile,
        string resultFile,
        string? progressFile = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Ids das operações elevadas suportadas pelo host.</summary>
public static class ElevatedOperationIds
{
    /// <summary>Executa um comando externo qualquer de forma elevada.</summary>
    public const string Command = "command";

    /// <summary>Executa uma otimização completa.</summary>
    public const string Optimization = "optimization";

    /// <summary>Executa uma limpeza.</summary>
    public const string Cleanup = "cleanup";

    /// <summary>Ativa/desativa o Modo Gamer.</summary>
    public const string GameMode = "gamemode";

    /// <summary>Aplica itens de privacidade.</summary>
    public const string Privacy = "privacy";

    /// <summary>Cria um ponto de restauração.</summary>
    public const string RestorePoint = "restorepoint";

    /// <summary>Opera sobre um serviço Windows.</summary>
    public const string Service = "service";

    /// <summary>Grava/bloqueia domínios no arquivo hosts.</summary>
    public const string Hosts = "hosts";
}

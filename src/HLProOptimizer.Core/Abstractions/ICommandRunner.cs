using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Execução padronizada de ferramentas externas do Windows
/// (powercfg, netsh, ipconfig, sfc, dism, reg, pnputil, chkdsk, MpCmdRun).
/// Centraliza timeout, captura de saída, logging e elevação.
/// </summary>
public interface ICommandRunner
{
    /// <summary>Executa um comando e captura stdout/stderr.</summary>
    /// <param name="fileName">Executável (caminho completo ou resolvido via PATH).</param>
    /// <param name="arguments">Argumentos.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <param name="elevated">Se deve executar com UAC elevado.</param>
    /// <param name="timeout">Tempo máximo de execução (padrão 5 minutos).</param>
    /// <param name="workingDirectory">Diretório de trabalho.</param>
    /// <param name="onOutputLine">Callback opcional para streaming de saída em tempo real.</param>
    Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default,
        bool elevated = false,
        TimeSpan? timeout = null,
        string? workingDirectory = null,
        Action<string>? onOutputLine = null);

    /// <summary>Executa um comando de forma oculta e descarta a saída (fire-and-forget seguro).</summary>
    /// <param name="fileName">Executável.</param>
    /// <param name="arguments">Argumentos.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<int> RunHiddenAsync(string fileName, string arguments, CancellationToken cancellationToken = default);
}

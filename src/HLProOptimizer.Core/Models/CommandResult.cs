namespace HLProOptimizer.Core.Models;

/// <summary>Resultado da execução de um comando externo.</summary>
/// <param name="FileName">Executável.</param>
/// <param name="Arguments">Argumentos.</param>
/// <param name="ExitCode">Código de saída.</param>
/// <param name="StandardOutput">stdout capturado.</param>
/// <param name="StandardError">stderr capturado.</param>
/// <param name="Duration">Tempo de execução.</param>
/// <param name="WasElevated">Se executou elevado.</param>
public sealed record CommandResult(
    string FileName,
    string Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool WasElevated)
{
    /// <summary>True quando o exit code indica sucesso.</summary>
    public bool IsSuccess => ExitCode == 0;

    /// <summary>Saída combinada (stdout + stderr).</summary>
    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : $"{StandardOutput}{Environment.NewLine}{StandardError}";
}

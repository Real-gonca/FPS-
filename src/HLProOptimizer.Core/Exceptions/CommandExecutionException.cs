namespace HLProOptimizer.Core.Exceptions;

/// <summary>
/// Lançada quando um comando externo (powercfg, netsh, sfc, dism, reg, pnputil)
/// retorna exit code de erro.
/// </summary>
public sealed class CommandExecutionException : OptimizerException
{
    /// <summary>Inicializa com os dados do comando executado.</summary>
    public CommandExecutionException(string fileName, string arguments, int exitCode, string output)
        : base($"O comando '{fileName} {arguments}' falhou com o código {exitCode}. Saída: {Truncate(output)}")
    {
        FileName = fileName;
        Arguments = arguments;
        ExitCode = exitCode;
        Output = output;
    }

    /// <summary>Executável invocado.</summary>
    public string FileName { get; }

    /// <summary>Argumentos usados.</summary>
    public string Arguments { get; }

    /// <summary>Código de saída do processo.</summary>
    public int ExitCode { get; }

    /// <summary>Saída combinada (stdout + stderr).</summary>
    public string Output { get; }

    private static string Truncate(string value, int max = 400)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";
}

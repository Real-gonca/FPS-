namespace HLProOptimizer.Core.Exceptions;

/// <summary>
/// Lançada quando uma análise/limpeza falha de forma não recuperável.
/// </summary>
public sealed class ScanException : OptimizerException
{
    /// <summary>Inicializa com a categoria/etapa que falhou.</summary>
    public ScanException(string stage, Exception? innerException = null)
        : base(innerException is null
            ? $"Falha na etapa de análise: {stage}."
            : $"Falha na etapa de análise: {stage}. Detalhes: {innerException.Message}", innerException!)
    {
        Stage = stage;
    }

    /// <summary>Etapa (categoria) que falhou.</summary>
    public string Stage { get; }
}

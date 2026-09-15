namespace HLProOptimizer.Core.Exceptions;

/// <summary>
/// Lançada quando uma operação exige privilégios de administrador e o processo
/// atual não está elevado. A UI captura esta exceção e oferece elevação
/// sob demanda via <c>IElevationService</c> (manifest é <c>asInvoker</c>).
/// </summary>
public sealed class ElevationRequiredException : OptimizerException
{
    /// <summary>Inicializa a exceção informando a operação bloqueada.</summary>
    /// <param name="operation">Nome/descrição da operação que exigia admin.</param>
    public ElevationRequiredException(string operation)
        : base($"A operação '{operation}' requer privilégios de administrador.")
    {
        Operation = operation;
    }

    /// <summary>Operação que exigia elevação.</summary>
    public string Operation { get; }
}

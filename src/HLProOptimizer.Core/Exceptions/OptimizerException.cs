namespace HLProOptimizer.Core.Exceptions;

/// <summary>
/// Exceção base de domínio do HL PRO OPTIMIZER.
/// Todas as falhas esperadas (disco inacessível, serviço recusando parada,
/// registro protegido) derivam dela, permitindo tratamento uniforme na UI.
/// </summary>
public class OptimizerException : Exception
{
    /// <summary>Inicializa uma nova instância com uma mensagem.</summary>
    public OptimizerException(string message)
        : base(message)
    {
    }

    /// <summary>Inicializa uma nova instância com mensagem e causa.</summary>
    public OptimizerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Inicializa uma nova instância padrão.</summary>
    public OptimizerException()
    {
    }
}

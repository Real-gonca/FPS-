using System.Diagnostics.CodeAnalysis;

namespace HLProOptimizer.Core.Common;

/// <summary>
/// Validações defensivas reutilizáveis (fail-fast). Mantém o código dos serviços
/// limpo e garante mensagens de erro consistentes em toda a solução.
/// </summary>
public static class Guard
{
    /// <summary>Garante que <paramref name="value"/> não seja nulo.</summary>
    /// <exception cref="ArgumentNullException">Quando <paramref name="value"/> é nulo.</exception>
    public static T NotNull<T>([NotNull] T? value, string? parameterName = null)
        where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        return value;
    }

    /// <summary>Garante que uma string não seja nula/vazia.</summary>
    /// <exception cref="ArgumentException">Quando a string é nula, vazia ou só espaços.</exception>
    public static string NotNullOrWhiteSpace([NotNull] string? value, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("O valor não pode ser nulo ou vazio.", parameterName);
        }

        return value;
    }

    /// <summary>Garante que <paramref name="value"/> esteja dentro de [min, max].</summary>
    /// <exception cref="ArgumentOutOfRangeException">Quando fora do intervalo.</exception>
    public static int InRange(int value, int min, int max, string? parameterName = null)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"O valor deve estar entre {min} e {max}.");
        }

        return value;
    }

    /// <summary>Garante que <paramref name="value"/> seja positivo.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Quando &lt;= 0.</exception>
    public static long Positive(long value, string? parameterName = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "O valor deve ser maior que zero.");
        }

        return value;
    }
}

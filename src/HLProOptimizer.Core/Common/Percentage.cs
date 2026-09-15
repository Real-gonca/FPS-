namespace HLProOptimizer.Core.Common;

/// <summary>
/// Utilitários de cálculo percentual usados pelo scoring e pelos gráficos.
/// </summary>
public static class Percentage
{
    /// <summary>
    /// Calcula a porcentagem de <paramref name="part"/> em relação a <paramref name="total"/>,
    /// limitada ao intervalo [0, 100]. Retorna 0 quando o total for inválido.
    /// </summary>
    public static double Of(double part, double total)
    {
        if (total <= 0 || double.IsNaN(part) || double.IsNaN(total))
        {
            return 0d;
        }

        return Clamp((part / total) * 100d);
    }

    /// <summary>Limita um valor percentual ao intervalo [0, 100].</summary>
    public static double Clamp(double value)
    {
        if (double.IsNaN(value))
        {
            return 0d;
        }

        return Math.Clamp(value, 0d, 100d);
    }

    /// <summary>Converte um percentual [0-100] em um valor normalizado [0-1].</summary>
    public static double Normalize(double value) => Clamp(value) / 100d;
}

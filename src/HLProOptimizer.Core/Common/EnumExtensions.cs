using System.ComponentModel;
using System.Reflection;

namespace HLProOptimizer.Core.Common;

/// <summary>Extensões utilitárias para enums.</summary>
public static class EnumExtensions
{
    /// <summary>
    /// Retorna o texto do atributo <see cref="DescriptionAttribute"/> do valor do enum,
    /// ou o próprio nome quando o atributo não existir.
    /// </summary>
    /// <param name="value">Valor do enum.</param>
    public static string GetDescription(this Enum value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var field = value.GetType().GetField(value.ToString());
        if (field?.GetCustomAttribute<DescriptionAttribute>() is { } description)
        {
            return description.Description;
        }

        return value.ToString();
    }

    /// <summary>Converte uma flag booleana em um dos dois valores informados.</summary>
    /// <typeparam name="T">Tipo de retorno.</typeparam>
    /// <param name="condition">Condição.</param>
    /// <param name="whenTrue">Valor quando verdadeiro.</param>
    /// <param name="whenFalse">Valor quando falso.</param>
    public static T ToValue<T>(this bool condition, T whenTrue, T whenFalse)
        => condition ? whenTrue : whenFalse;
}

namespace Nexus.Domain.Metrics;

/// <summary>
/// Valor medido do sistema com disponibilidade explícita.
///
/// REGRA DE OURO (spec §0.2 / §2.3): se a fonte real do sistema não existir ou
/// falhar, <see cref="IsAvailable"/> é false e a UI mostra <see cref="NotAvailable"/>
/// ("N/D") — nunca um número inventado.
/// </summary>
public readonly record struct MetricValue(double? Value)
{
    /// <summary>Texto canónico para valores indisponíveis.</summary>
    public const string NotAvailable = "N/D";

    public bool IsAvailable => Value is not null;

    /// <summary>Wraps a measured value; null → N/D (fonte indisponível).</summary>
    public static MetricValue Of(double? value) => new(value);

    public static MetricValue Missing() => new(null);

    /// <summary>Formata em pt-PT (idioma base do produto). Ex.: "1.234,5".</summary>
    public string Format(string format = "0")
    {
        return Value is null
            ? NotAvailable
            : Value.Value.ToString(format, System.Globalization.CultureInfo.GetCultureInfo("pt-PT"));
    }
}

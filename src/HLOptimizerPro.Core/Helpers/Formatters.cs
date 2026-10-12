using System.Globalization;

namespace HLOptimizerPro.Core.Helpers;

/// <summary>
/// Formatação de valores de sistema para apresentação em português (pt-PT/pt-AO).
/// Usa vírgula decimal, separador de milhares por espaço fino e unidades SI (base 1024).
/// </summary>
public static class Formatters
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    /// <summary>Ex.: 17179869184 → "16,0 GB".</summary>
    public static string Bytes(long bytes, int decimals = 1)
    {
        if (bytes < 0) bytes = 0;
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // Em bytes não faz sentido mostrar casas decimais.
        var decimalsEffective = unit == 0 ? 0 : decimals;
        return string.Format(Pt, "{0:N" + decimalsEffective + "} {1}", value, units[unit]);
    }

    /// <summary>Ex.: 12000000000 → "11,2 GB/seg" (usado em cache/velocidade de disco).</summary>
    public static string BytesPerSecond(long bytesPerSecond) => Bytes(bytesPerSecond) + "/seg";

    /// <summary>Ex.: 3400 → "3,40 GHz".</summary>
    public static string Frequency(double megaHertz)
    {
        if (megaHertz <= 0) return "—";
        return megaHertz >= 1000
            ? string.Format(Pt, "{0:N2} GHz", megaHertz / 1000.0)
            : string.Format(Pt, "{0:N0} MHz", megaHertz);
    }

    /// <summary>Ex.: 0.237 → "24 %" (percentagem inteira, sem casas, arredondada).</summary>
    public static string Percent(double fraction, int decimals = 0, bool alreadyInPercent = false)
    {
        var value = alreadyInPercent ? fraction : fraction * 100.0;
        if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
        value = Math.Clamp(value, 0, 100);
        return string.Format(Pt, "{0:N" + decimals + "} %", value);
    }

    /// <summary>Ex.: 47.3 → "47,3 ms".</summary>
    public static string Milliseconds(double ms, int decimals = 1)
    {
        if (ms <= 0) return "—";
        return string.Format(Pt, "{0:N" + decimals + "} ms", ms);
    }

    /// <summary>Ex.: 3.7 → "3,7 °C".</summary>
    public static string Temperature(double? celsius)
        => celsius is null or <= 0 ? "—" : string.Format(Pt, "{0:N0} °C", celsius.Value);

    /// <summary>Duração legível: "2 d 4 h 17 m".</summary>
    public static string Uptime(TimeSpan span)
    {
        if (span.TotalSeconds < 0) span = TimeSpan.Zero;
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays} d {span.Hours} h {span.Minutes} m";
        if (span.TotalHours >= 1) return $"{span.Hours} h {span.Minutes} m";
        return $"{span.Minutes} m {span.Seconds} s";
    }

    /// <summary>Data longa em português: "quarta-feira, 4 de outubro de 2026".</summary>
    public static string LongDate(DateTime value)
        => value.ToString("dddd, d 'de' MMMM 'de' yyyy", Pt);

    /// <summary>Hora curta fixa: "14:07".</summary>
    public static string ShortTime(DateTime value) => value.ToString("HH:mm", Pt);

    /// <summary>Contagem de núcleos/threads: "8 núcleos / 16 threads".</summary>
    public static string Cores(int cores, int threads) => $"{cores} núcleos / {threads} threads";

    /// <summary>Pontuação 0–100 com uma casa: "87,4 %".</summary>
    public static string Score(double value0To100) => string.Format(Pt, "{0:N1} %", Math.Clamp(value0To100, 0, 100));

    /// <summary>
    /// Classificação textual da pontuação global (usada em Avaliação e no cabeçalho do Dashboard).
    /// Limites definidos em <see cref="AppConstants"/> para ficarem auditáveis num só sítio.
    /// </summary>
    public static string ScoreGradeLabel(double value0To100) => value0To100 switch
    {
        >= 85 => "Excelente",
        >= 70 => "Bom",
        >= 50 => "Regular",
        _ => "Crítico",
    };
}

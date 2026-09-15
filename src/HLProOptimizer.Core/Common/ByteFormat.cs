using System.Globalization;

namespace HLProOptimizer.Core.Common;

/// <summary>
/// Formatação humana de bytes e de intervalos de tempo. Usada tanto pela UI
/// (via converters) quanto pelos relatórios/log.
/// </summary>
public static class ByteFormat
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// Converte bytes em texto legível (ex.: 1536 -&gt; "1,50 KB").
    /// </summary>
    /// <param name="bytes">Quantidade de bytes (valores negativos são tratados como 0).</param>
    /// <param name="decimals">Número de casas decimais.</param>
    /// <param name="culture">Cultura usada na formatação (padrão: cultura atual).</param>
    public static string Format(long bytes, int decimals = 2, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (bytes <= 0)
        {
            return $"0 {Units[0]}";
        }

        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? string.Format(culture, "{0:F0} {1}", value, Units[unitIndex])
            : string.Format(culture, "{0:F" + decimals.ToString(CultureInfo.InvariantCulture) + "} {1}", value, Units[unitIndex]);
    }

    /// <summary>Converte MB/s em texto legível.</summary>
    public static string FormatRate(double bytesPerSecond, CultureInfo? culture = null)
        => $"{Format((long)Math.Round(bytesPerSecond), 1, culture)}/s";

    /// <summary>
    /// Formata um <see cref="TimeSpan"/> como uptime amigável (ex.: "2d 04h 13m").
    /// </summary>
    public static string FormatUptime(TimeSpan uptime, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }

        if (uptime.TotalDays >= 1)
        {
            return string.Format(culture, "{0}d {1:D2}h {2:D2}m", (int)uptime.TotalDays, uptime.Hours, uptime.Minutes);
        }

        if (uptime.TotalHours >= 1)
        {
            return string.Format(culture, "{0:D2}h {1:D2}m", uptime.Hours, uptime.Minutes);
        }

        return string.Format(culture, "{0:D2}m {1:D2}s", uptime.Minutes, uptime.Seconds);
    }

    /// <summary>Formata megabits por segundo com 1 casa decimal.</summary>
    public static string FormatMbps(double megabitsPerSecond, CultureInfo? culture = null)
        => string.Format(culture ?? CultureInfo.CurrentCulture, "{0:F1} Mbps", megabitsPerSecond);
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Presentation.Localization;

namespace HLProOptimizer.Presentation.Converters;

/// <summary>
/// Converte <see cref="bool"/> em <see cref="Visibility"/>.
/// </summary>
/// <remarks>Parâmetro <c>"Invert"</c> inverte o resultado (true → Collapsed).</remarks>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool boolean && boolean;

        if (IsInverted(parameter))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is Visibility visibility && visibility == Visibility.Visible;

        return IsInverted(parameter) ? !visible : visible;
    }

    private static bool IsInverted(object? parameter) =>
        parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Inverte um valor booleano (usado em <c>IsEnabled="{Binding IsBusy, Converter=...}"</c>).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>Converte <see cref="bool"/> em opacidade (1 ou 0,35) para estados desabilitados.</summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1d : 0.35d;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double opacity && opacity > 0.7d;
}

/// <summary><c>null</c>/vazio → Collapsed; com conteúdo → Visible.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            System.Collections.ICollection collection => collection.Count > 0,
            _ => true
        };

        if (parameter is string flag && flag.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formata bytes em texto legível (KB/MB/GB) usando o utilitário do Core.</summary>
public sealed class BytesToTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytes = value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            double doubleValue => (long)doubleValue,
            string text when long.TryParse(text, NumberStyles.Integer, culture, out var parsed) => parsed,
            _ => 0L
        };

        var decimals = parameter is string digits && int.TryParse(digits, out var parsedDigits) ? parsedDigits : 1;

        return bytes <= 0 ? "0 B" : ByteFormat.Format(bytes, decimals);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formata um percentual (0-100) com uma casa decimal.</summary>
public sealed class PercentToTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            _ => 0d
        };

        return $"{percent:0.#}%";
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Escolhe um pincel do tema conforme um percentual/valor (verde → âmbar → vermelho).
/// </summary>
/// <remarks>
/// Os pincéis vêm do <see cref="Application"/> (não de cores fixas) para respeitar o
/// tema ativo. Parâmetro opcional define o modo: "Score" (maior é melhor) ou
/// "Usage" (maior é pior, padrão).
/// </remarks>
public sealed class PercentToBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double doubleValue => doubleValue,
            int intValue => intValue,
            float floatValue => floatValue,
            long longValue => longValue,
            _ => 0d
        };

        var isScore = parameter is string mode && mode.Equals("Score", StringComparison.OrdinalIgnoreCase);

        // Para pontuação, invertemos a escala (80+ = bom).
        var effective = isScore ? percent : 100d - percent;

        var key = effective switch
        {
            >= 70 => "Brush.Success",
            >= 40 => "Brush.Warning",
            _ => "Brush.Danger"
        };

        return FindBrush(key);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static Brush FindBrush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>
/// Mapeia <see cref="Severity"/> para o pincel semântico correspondente.
/// Use <c>ConverterParameter=Soft</c> para obter a variante translúcida (fundo de badge).
/// </summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var severity = value switch
        {
            Severity typed => typed,
            string text when Enum.TryParse<Severity>(text, ignoreCase: true, out var parsed) => parsed,
            int number => (Severity)number,
            _ => Severity.Low
        };

        // ConverterParameter="Soft" devolve a variante translúcida (fundos de badge),
        // mantendo a cor sólida para o texto — contraste garantido no tema dark.
        var soft = string.Equals(parameter?.ToString(), "Soft", StringComparison.OrdinalIgnoreCase);

        var key = severity switch
        {
            Severity.Critical => soft ? "Brush.Danger.Soft" : "Brush.Danger",
            Severity.High => soft ? "Brush.Danger.Soft" : "Brush.Danger",
            Severity.Medium => soft ? "Brush.Warning.Soft" : "Brush.Warning",
            Severity.Low => soft ? "Brush.Info.Soft" : "Brush.Info",
            _ => soft ? "Brush.Glass" : "Brush.Text.Muted"
        };

        return PercentToBrushConverter.FindBrush(key);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Traduz um <see cref="Enum"/> usando as chaves <c>Enum_&lt;Tipo&gt;_&lt;Valor&gt;</c>
/// do serviço de localização — e reage à troca de idioma em runtime.
/// </summary>
public sealed class EnumToLocalizedNameConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => string.Empty,
            Enum enumeration => LocalizationProxy.Instance.GetEnumText(enumeration),
            _ => value.ToString() ?? string.Empty
        };

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Texto relativo em PT-BR para datas recentes ("agora", "há 5 min", "ontem").</summary>
public sealed class RelativeTimeConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime timestamp || timestamp == default)
        {
            return LocalizationProxy.Instance["Common_NotAvailable"];
        }

        var elapsed = DateTime.Now - timestamp;

        if (elapsed.TotalSeconds < 60)
        {
            return "agora";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"há {(int)elapsed.TotalMinutes} min";
        }

        if (elapsed.TotalHours < 24)
        {
            return $"há {(int)elapsed.TotalHours} h";
        }

        if (elapsed.TotalDays < 2)
        {
            return "ontem";
        }

        if (elapsed.TotalDays < 7)
        {
            return $"há {(int)elapsed.TotalDays} dias";
        }

        return timestamp.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formata um <see cref="TimeSpan"/> como "1 h 23 min" (uptime) ou "mm:ss" (durações).</summary>
public sealed class TimeSpanToTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan duration)
        {
            return "-";
        }

        var compact = parameter is string mode && mode.Equals("Short", StringComparison.OrdinalIgnoreCase);

        if (compact)
        {
            return duration.ToString(duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        }

        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays} d {duration.Hours} h";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} h {duration.Minutes} min";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes} min";
        }

        return $"{(int)duration.TotalSeconds} s";
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converte bytes/segundo em taxa legível ("12,4 MB/s").</summary>
public sealed class RateToTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytesPerSecond = value switch
        {
            double doubleValue => doubleValue,
            long longValue => longValue,
            int intValue => intValue,
            _ => 0d
        };

        return $"{ByteFormat.Format((long)Math.Max(0, bytesPerSecond), 1)}/s";
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}


/// <summary>
/// Converte uma string em <see cref="bool"/> comparando com o <c>ConverterParameter</c>.
/// </summary>
/// <remarks>
/// Usado no menu lateral para marcar o item da tela atual:
/// <c>IsChecked="{Binding CurrentKey, Converter={StaticResource Conv.StringEquals}, ConverterParameter=dashboard}"</c>.
/// </remarks>
public sealed class StringEqualsToBoolConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter?.ToString() ?? string.Empty : Binding.DoNothing;
}

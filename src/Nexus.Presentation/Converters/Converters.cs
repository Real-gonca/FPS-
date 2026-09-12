using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Nexus.Domain.Optimization;

namespace Nexus.Presentation.Converters;

/// <summary>
/// Faixas do Score (spec §3.2: "cor dinâmica conforme faixa").
/// Cores = paleta canónica (spec §3.1): sucesso/ciano/aviso/erro.
/// </summary>
public static class ScoreBands
{
    public static (Color Color, string Band) For(double score) => score switch
    {
        >= 85 => (Color.FromRgb(0x34, 0xD3, 0x99), "Excelente"),
        >= 70 => (Color.FromRgb(0x22, 0xD3, 0xEE), "Boa"),
        >= 45 => (Color.FromRgb(0xFB, 0xBF, 0x24), "Média"),
        _ => (Color.FromRgb(0xF8, 0x71, 0x71), "Baixa"),
    };

    public static Brush BrushFor(double? score)
    {
        Color color = score is null
            ? Color.FromRgb(0x8B, 0x93, 0xB0)
            : For(Math.Clamp(score.Value, 0, 100)).Color;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static string BandText(double? score) =>
        score is null ? "N/D" : For(Math.Clamp(score.Value, 0, 100)).Band;
}

public sealed class ScoreToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ScoreBands.BrushFor(value as double?);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Disponibilidade de métrica: disponível → texto primário; N/D → secundário.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool available = value is true;
        var brush = new SolidColorBrush(available
            ? Color.FromRgb(0xE5, 0xE9, 0xF5)
            : Color.FromRgb(0x8B, 0x93, 0xB0));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RiskToBrushConverter : IValueConverter
{
    public static Color RiskColor(RiskLevel risk) => risk switch
    {
        RiskLevel.Low => Color.FromRgb(0x34, 0xD3, 0x99),
        RiskLevel.Medium => Color.FromRgb(0xFB, 0xBF, 0x24),
        _ => Color.FromRgb(0xF8, 0x71, 0x71),
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = RiskColor(value is RiskLevel risk ? risk : RiskLevel.Low);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Fundo semi-transparente do badge de risco.</summary>
public sealed class RiskToBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = RiskToBrushConverter.RiskColor(value is RiskLevel risk ? risk : RiskLevel.Low);
        return new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Cor de acento por tipo de notificação.</summary>
public sealed class KindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not NotificationKind kind)
            return Brushes.Gray;

        var color = kind switch
        {
            NotificationKind.Success => Color.FromRgb(0x34, 0xD3, 0x99),
            NotificationKind.Warning => Color.FromRgb(0xFB, 0xBF, 0x24),
            NotificationKind.Error => Color.FromRgb(0xF8, 0x71, 0x71),
            _ => Color.FromRgb(0x22, 0xD3, 0xEE),
        };

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverte um bool (ex.: habilitar botão enquanto NÃO está elevado).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>null → false; caso contrário true (ex.: habilitar botão só com seleção).</summary>
public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Opções do filtro de estado no Serviços Manager.</summary>
public static class StateFilterOptions
{
    public static string[] All { get; } = { "Todos", "Em execução", "Parados", "Desativados" };
}

/// <summary>
/// Fundo dos segmentos do seletor de modo.
/// value = IsAdvanced (bool); parameter = "simple" | "advanced".
/// </summary>
public sealed class SegmentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isAdvanced = value is true;
        bool wantsAdvanced = parameter is string s && s == "advanced";
        return isAdvanced == wantsAdvanced
            ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
            : Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

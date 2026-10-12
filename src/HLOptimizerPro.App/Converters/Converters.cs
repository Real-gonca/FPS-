using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using MahApps.Metro.IconPacks;

namespace HLOptimizerPro.App.Converters;

/// <summary>bool → Visibility (com parâmetro "inverter" para negar).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (IsInverted(parameter)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible ^ IsInverted(parameter);

    /// <summary>O parâmetro "inverter" (ou "inverse" / "!") nega a condição.</summary>
    internal static bool IsInverted(object? parameter)
        => parameter is string text
           && (text.Equals("inverter", StringComparison.OrdinalIgnoreCase)
               || text.Equals("inverse", StringComparison.OrdinalIgnoreCase)
               || text == "!");
}

/// <summary>bool → Visibility invertido (atalho usado em vários sítios).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not Visibility.Visible;
}

/// <summary>bool → bool invertido (usado em IsEnabled = !IsBusy).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>null/vazio → Visibility (mostra avisos quando não há dados).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = value is null || (value is string text && string.IsNullOrWhiteSpace(text));
        if (BoolToVisibilityConverter.IsInverted(parameter)) isEmpty = !isEmpty;
        return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Mostra o elemento quando a coleção tem itens (empty state handling).</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int number => number,
            System.Collections.ICollection collection => collection.Count,
            null => 0,
            _ => 1,
        };

        var visible = count > 0;
        if (BoolToVisibilityConverter.IsInverted(parameter)) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// string → PackIconKind.
///
/// Porque é que isto existe? Porque os modelos guardam o nome do ícone como texto (ex.: "Speedometer"),
/// o que mantém a camada Core independente do pacote de ícones. Se o nome não existir na versão
/// instalada do pacote, mostramos um ícone neutro em vez de rebentar o XAML.
/// </summary>
public sealed class IconKindConverter : IValueConverter
{
    private static readonly PackIconMaterialKind Fallback = PackIconMaterialKind.TuneVariant;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PackIconMaterialKind kind) return kind;
        if (value is not string text || string.IsNullOrWhiteSpace(text)) return Fallback;

        return Enum.TryParse<PackIconMaterialKind>(text, ignoreCase: true, out var parsed) ? parsed : Fallback;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() ?? string.Empty;
}

/// <summary>HealthLevel → brush (verde/amarelo/vermelho/azul).</summary>
public sealed class HealthToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            HealthLevel.Otimo => "BrushSuccess",
            HealthLevel.Atencao => "BrushWarning",
            HealthLevel.Critico => "BrushDanger",
            _ => "BrushInfo",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.SteelBlue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>HealthLevel → cor suave de fundo (badges e cartões).</summary>
public sealed class HealthToSoftBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            HealthLevel.Otimo => "BrushSuccessSoft",
            HealthLevel.Atencao => "BrushWarningSoft",
            HealthLevel.Critico => "BrushDangerSoft",
            _ => "BrushInfoSoft",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>NotificationLevel → brush (toasts).</summary>
public sealed class NotificationToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            NotificationLevel.Sucesso => "BrushSuccess",
            NotificationLevel.Aviso => "BrushWarning",
            NotificationLevel.Erro => "BrushDanger",
            _ => "BrushAccentBright",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.SteelBlue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// RecommendationPriority → brush. O parâmetro "soft" devolve o tom suave (fundos),
/// qualquer outro valor devolve a cor forte (bordas, ícones e etiquetas).
/// </summary>
public sealed class PriorityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var soft = parameter is string text && text.Equals("soft", StringComparison.OrdinalIgnoreCase);

        var key = value switch
        {
            RecommendationPriority.Alta => soft ? "BrushDangerSoft" : "BrushDanger",
            RecommendationPriority.Media => soft ? "BrushWarningSoft" : "BrushWarning",
            _ => soft ? "BrushSuccessSoft" : "BrushSuccess",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.SteelBlue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Valor igual ao parâmetro → true (usado para destacar o perfil de tweaks ativo).</summary>
public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter ?? Binding.DoNothing : Binding.DoNothing;
}

/// <summary>Nível de uso (0–1) → brush de saúde para gauges e barras.</summary>
public sealed class UsageToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var usage = value is double number ? number : 0;

        var key = usage switch
        {
            < 0.70 => "BrushSuccess",
            < 0.85 => "BrushWarning",
            _ => "BrushDanger",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.SteelBlue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Percentagem (0–100) → brush de estado (barras de sub-pontuação).</summary>
public sealed class ScoreToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var score = value is double number ? number : 0;

        var key = score switch
        {
            >= 85 => "BrushSuccess",
            >= 65 => "BrushWarning",
            _ => "BrushDanger",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.SteelBlue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Data → texto relativo ("há 2 minutos", "ontem").</summary>
public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime timestamp) return string.Empty;

        var delta = DateTime.Now - timestamp;

        return delta switch
        {
            { TotalSeconds: < 60 } => "agora mesmo",
            { TotalMinutes: < 60 } => $"há {(int)delta.TotalMinutes} min",
            { TotalHours: < 24 } => $"há {(int)delta.TotalHours} h",
            { TotalDays: < 2 } => "ontem",
            { TotalDays: < 30 } => $"há {(int)delta.TotalDays} dias",
            _ => timestamp.ToString("dd/MM/yyyy"),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Esconde o texto quando está vazio (evita linhas em branco em listas).</summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

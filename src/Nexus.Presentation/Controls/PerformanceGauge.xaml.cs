using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Nexus.Presentation.Converters;

namespace Nexus.Presentation.Controls;

/// <summary>
/// Gauge circular (0–100) com cor dinâmica por faixa.
/// <see cref="Value"/> = null → apresenta "N/D" (regra de ouro: sem dado real,
/// nunca um número inventado).
/// </summary>
public partial class PerformanceGauge : UserControl
{
    private const double CenterX = 100;
    private const double CenterY = 100;
    private const double Radius = 82;

    /// <summary>Ângulo inicial (espaço "relojoideiro", 0 = 12h): 135° = 7:30.</summary>
    private const double StartAngle = 135;

    /// <summary>Varredura total: 270° (termina em 1:30).</summary>
    private const double SweepAngle = 270;

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double?),
            typeof(PerformanceGauge),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    /// <summary>Score atual (0–100) ou null → N/D.</summary>
    public double? Value
    {
        get => (double?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public PerformanceGauge()
    {
        InitializeComponent();
        TrackArc.Data = BuildArcGeometry(0, 100);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PerformanceGauge)d).UpdateArc(e.NewValue is double v ? v : (double?)null);
    }

    private void UpdateArc(double? score)
    {
        if (score is null)
        {
            ValueArc.Data = null;
            ValueText.Text = "N/D";
            BandText.Text = "sem dados reais suficientes";
            return;
        }

        double v = Math.Clamp(score.Value, 0, 100);
        (Color color, string band) = ScoreBands.For(v);

        ValueArc.Data = v <= 0.5 ? null : BuildArcGeometry(0, v);
        ValueArc.Stroke = new SolidColorBrush(color);
        ValueText.Text = ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
        ValueText.Foreground = new SolidColorBrush(color);
        BandText.Text = band;

        if (ValueArc.Effect is DropShadowEffect glow)
            glow.Color = color;
    }

    private static Geometry BuildArcGeometry(double from, double to)
    {
        Point PointAt(double angleDegrees) => new(
            CenterX + Radius * Math.Sin(angleDegrees * Math.PI / 180.0),
            CenterY - Radius * Math.Cos(angleDegrees * Math.PI / 180.0));

        var start = PointAt(StartAngle + SweepAngle * from / 100.0);
        var end = PointAt(StartAngle + SweepAngle * to / 100.0);

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            EndPoint = end,
            Size = new Size(Radius, Radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = (to - from) / 100.0 > 0.5,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}

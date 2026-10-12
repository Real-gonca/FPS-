using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace HLOptimizerPro.App.Controls;

/// <summary>
/// Gauge circular desenhado diretamente (sem imagens), usado no Dashboard para CPU, RAM e GPU.
///
/// Detalhes de implementação:
///   • O arco vai de 135° a 405° (270° de amplitude), o que dá o aspeto de "manómetro" do design.
///   • O valor é animado até ao alvo com um temporizador leve (60 passos por segundo, mas apenas
///     enquanto há diferença) — a interface não trava e o número mostrado é sempre o real.
///   • A cor do arco vem do <see cref="AccentBrush"/>, o que permite gradientes por métrica
///     (azul para CPU, roxo para RAM, verde para GPU) definidos no design system.
/// </summary>
public class GaugeControl : FrameworkElement
{
    private double _displayedValue;
    private DispatcherTimer? _timer;
    private double _targetValue;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(GaugeControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

    /// <summary>Valor atual (0–100).</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(GaugeControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Rótulo por cima do valor (ex.: "CPU").</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(GaugeControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Texto central (ex.: "42 %"). Vem sempre de uma medição real.</summary>
    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public static readonly DependencyProperty SubTextProperty = DependencyProperty.Register(
        nameof(SubText), typeof(string), typeof(GaugeControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Texto secundário (ex.: "9,2 / 16 GB").</summary>
    public string SubText
    {
        get => (string)GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(GaugeControl),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x2D, 0x7B, 0xFF)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(GaugeControl),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x30, 0x8F, 0xA3, 0xC8)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Cor do arco de fundo.</summary>
    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(GaugeControl),
        new FrameworkPropertyMetadata(11d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Espessura do arco.</summary>
    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public static readonly DependencyProperty AnimatedProperty = DependencyProperty.Register(
        nameof(Animated), typeof(bool), typeof(GaugeControl), new PropertyMetadata(true));

    /// <summary>Respeita a preferência de animações do utilizador (Configuração).</summary>
    public bool Animated
    {
        get => (bool)GetValue(AnimatedProperty);
        set => SetValue(AnimatedProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GaugeControl gauge) return;

        gauge._targetValue = Math.Clamp((double)e.NewValue, 0, 100);

        if (!gauge.Animated || !gauge.IsLoaded)
        {
            gauge._displayedValue = gauge._targetValue;
            gauge.InvalidateVisual();
            return;
        }

        gauge.StartAnimation();
    }

    private void StartAnimation()
    {
        _timer ??= new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };

        if (!_timer.IsEnabled)
        {
            _timer.Tick += OnTick;
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var difference = _targetValue - _displayedValue;

        // Aproximação exponencial: rápida no início, suave no fim.
        _displayedValue += difference * 0.18;

        if (Math.Abs(difference) < 0.4)
        {
            _displayedValue = _targetValue;
            _timer?.Stop();
            if (_timer is not null) _timer.Tick -= OnTick;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 8) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - Thickness) / 2 - 2;
        if (radius <= 2) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // ---- Arco de fundo -------------------------------------------------
        DrawArc(dc, center, radius, 135, 405, TrackBrush, Thickness);

        // ---- Arco de valor -------------------------------------------------
        var sweep = 270 * (_displayedValue / 100.0);
        if (sweep > 0.4)
            DrawArc(dc, center, radius, 135, 135 + sweep, AccentBrush, Thickness);

        // ---- Textos --------------------------------------------------------
        var titleText = MakeText(Title, size * 0.085, Brush("BrushTextSecondary"), dpi, FontWeights.Medium);
        if (titleText is not null)
            dc.DrawText(titleText, new Point(center.X - titleText.Width / 2, center.Y - radius * 0.62));

        var valueText = MakeText(ValueText, size * 0.185, Brush("BrushTextPrimary"), dpi, FontWeights.Bold);
        if (valueText is not null)
            dc.DrawText(valueText, new Point(center.X - valueText.Width / 2, center.Y - valueText.Height * 0.55));

        var subText = MakeText(SubText, size * 0.072, Brush("BrushTextMuted"), dpi, FontWeights.Normal);
        if (subText is not null)
            dc.DrawText(subText, new Point(center.X - subText.Width / 2, center.Y + valueText!.Height * 0.42));
    }

    private static Brush Brush(string resourceKey)
        => Application.Current?.TryFindResource(resourceKey) as Brush ?? Brushes.Silver;

    private static FormattedText? MakeText(string text, double size, Brush brush, double dpi, FontWeight weight)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        return new FormattedText(
            text,
            CultureInfo.GetCultureInfo("pt-PT"),
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI, Inter"), FontStyles.Normal, weight, FontStretches.Normal),
            Math.Max(9, size),
            brush,
            dpi);
    }

    /// <summary>Desenha um arco entre dois ângulos (graus, 0° = direita, sentido horário no ecrã).</summary>
    private static void DrawArc(DrawingContext dc, Point center, double radius,
        double startAngle, double endAngle, Brush brush, double thickness)
    {
        Point PointAt(double angle)
        {
            var radians = angle * Math.PI / 180.0;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }

        var start = PointAt(startAngle);
        var end = PointAt(endAngle);

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            RotationAngle = 0,
            IsLargeArc = endAngle - startAngle > 180,
            SweepDirection = SweepDirection.Clockwise,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        dc.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>
/// Barra de progresso fina usada nas listas (processos, sub-pontuações).
/// Foi feita como controlo próprio para garantir o raio e o gradiente do design system.
/// </summary>
public class MetricBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(MetricBar),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Percentagem 0–100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(MetricBar),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x2D, 0x7B, 0xFF)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Cor do preenchimento.</summary>
    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var height = ActualHeight > 0 ? ActualHeight : 6;
        var radius = height / 2;

        var track = new SolidColorBrush(Color.FromArgb(0x2E, 0x8F, 0xA3, 0xC8));
        dc.DrawRoundedRectangle(track, null, new Rect(0, 0, ActualWidth, height), radius, radius);

        var width = ActualWidth * (Math.Clamp(Value, 0, 100) / 100.0);
        if (width <= 0.5) return;

        dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, width, height), radius, radius);
    }
}

/// <summary>
/// Anel circular de pontuação (usado na Avaliação e no Dashboard).
/// Mostra o valor em percentagem no centro e pinta o arco conforme a saúde do sistema.
/// </summary>
public class ScoreRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ScoreRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

    /// <summary>Pontuação 0–100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(
        nameof(RingBrush), typeof(Brush), typeof(ScoreRing),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x2D, 0x7B, 0xFF)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Cor do anel (verde/amarelo/vermelho conforme a pontuação).</summary>
    public Brush RingBrush
    {
        get => (Brush)GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(ScoreRing),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Legenda por baixo do número (ex.: "Pontuação global").</summary>
    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    private double _displayed = -1;
    private double _target;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScoreRing ring) return;

        ring._target = Math.Clamp((double)e.NewValue, 0, 100);

        if (!ring.IsLoaded)
        {
            ring._displayed = ring._target;
            ring.InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 12) return;

        if (_displayed < 0)
        {
            _displayed = _target;
        }
        else if (Math.Abs(_displayed - _target) > 0.4)
        {
            // Animação simples: cada render aproxima-se um pouco do alvo.
            _displayed += (_target - _displayed) * 0.12;
            InvalidateVisual();
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var thickness = Math.Max(8, size * 0.055);
        var radius = size / 2 - thickness / 2 - 2;

        var track = new SolidColorBrush(Color.FromArgb(0x2E, 0x8F, 0xA3, 0xC8));
        DrawArc(dc, center, radius, 135, 405, track, thickness);

        var sweep = 270 * (_displayed / 100.0);
        if (sweep > 0.4)
            DrawArc(dc, center, radius, 135, 135 + sweep, RingBrush, thickness);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var valueBrush = Application.Current?.TryFindResource("BrushTextPrimary") as Brush ?? Brushes.White;
        var captionBrush = Application.Current?.TryFindResource("BrushTextMuted") as Brush ?? Brushes.Gray;

        var valueText = new FormattedText(
            $"{_displayed:0} %",
            CultureInfo.GetCultureInfo("pt-PT"),
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI, Inter"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            size * 0.21,
            valueBrush,
            dpi);

        dc.DrawText(valueText, new Point(center.X - valueText.Width / 2, center.Y - valueText.Height * 0.58));

        if (!string.IsNullOrWhiteSpace(Caption))
        {
            var captionText = new FormattedText(
                Caption,
                CultureInfo.GetCultureInfo("pt-PT"),
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI, Inter"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                size * 0.07,
                captionBrush,
                dpi);

            dc.DrawText(captionText, new Point(center.X - captionText.Width / 2, center.Y + valueText.Height * 0.5));
        }
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius,
        double startAngle, double endAngle, Brush brush, double thickness)
    {
        Point PointAt(double angle)
        {
            var radians = angle * Math.PI / 180.0;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }

        var figure = new PathFigure { StartPoint = PointAt(startAngle), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = PointAt(endAngle),
            Size = new Size(radius, radius),
            RotationAngle = 0,
            IsLargeArc = endAngle - startAngle > 180,
            SweepDirection = SweepDirection.Clockwise,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        dc.DrawGeometry(null, new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PrecisionCurve.Controls
{
    /// <summary>
    /// Renders the acceleration curve: X = input speed (counts/ms),
    /// Y = output sensitivity (multiplier).
    ///
    /// Drawn by hand instead of with a charting package because the shape of
    /// this plot is fixed and specific: a unity reference line, a gradient fill
    /// under the curve, axis labels in sensor units, and a live dot showing the
    /// gain the kernel applied to the last packet.
    /// </summary>
    public sealed class CurveGraph : FrameworkElement
    {
        private static readonly Typeface AxisFace =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static readonly Typeface LabelFace =
            new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal,
                         FontWeights.Normal, FontStretches.Normal);

        private static readonly Brush GridBrush = Frozen(Color.FromRgb(0x22, 0x2A, 0x35));
        private static readonly Brush SoftGridBrush = Frozen(Color.FromRgb(0x1A, 0x21, 0x2A));
        private static readonly Brush AxisBrush = Frozen(Color.FromRgb(0x3A, 0x46, 0x54));
        private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0x8B, 0x98, 0xA5));
        private static readonly Brush UnityBrush = Frozen(Color.FromArgb(0x80, 0xF8, 0x81, 0x49));
        private static readonly Brush LiveBrush = Frozen(Color.FromRgb(0x3F, 0xB9, 0x50));
        private static readonly Brush HoverBrush = Frozen(Color.FromArgb(0x60, 0x4F, 0x9C, 0xF9));

        private static readonly Pen CurvePen = FrozenPen(Color.FromRgb(0x4F, 0x9C, 0xF9), 2.2);
        private static readonly Pen GlowPen = FrozenPen(Color.FromArgb(0x55, 0x4F, 0x9C, 0xF9), 6.0);
        private static readonly Pen UnityPen = FrozenPen(Color.FromArgb(0x90, 0xF8, 0x81, 0x49), 1.0, true);
        private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0x22, 0x2A, 0x35), 1.0);
        private static readonly Pen AxisPen = FrozenPen(Color.FromRgb(0x3A, 0x46, 0x54), 1.0);
        private static readonly Pen LivePen = FrozenPen(Color.FromRgb(0x3F, 0xB9, 0x50), 1.0, true);
        private static readonly Pen HoverPen = FrozenPen(Color.FromArgb(0x60, 0x4F, 0x9C, 0xF9), 1.0);

        private IReadOnlyList<(double Speed, double Gain)> _points = Array.Empty<(double, double)>();
        private double _maxSpeed = 100.0;
        private double _maxGain = 2.0;
        private double _liveSpeed;
        private double _liveGain = 1.0;
        private bool _hasLive;
        private double? _hoverSpeed;

        private const double PadLeft = 48;
        private const double PadRight = 14;
        private const double PadTop = 16;
        private const double PadBottom = 28;

        public CurveGraph()
        {
            SnapsToDevicePixels = true;
            ClipToBounds = true;
            Cursor = Cursors.Cross;
        }

        public void SetCurve(IReadOnlyList<(double Speed, double Gain)> points, double maxSpeed)
        {
            _points = points ?? Array.Empty<(double, double)>();
            _maxSpeed = Math.Max(0.5, maxSpeed);

            double peak = 1.0;
            foreach ((double _, double gain) in _points)
            {
                if (gain > peak) peak = gain;
            }

            _maxGain = Math.Max(1.25, Math.Ceiling(peak * 2.0) / 2.0);
            InvalidateVisual();
        }

        public void SetLive(double speed, double gain, bool hasLive)
        {
            _hasLive = hasLive;
            _liveSpeed = speed;
            _liveGain = gain;
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            Point position = e.GetPosition(this);
            if (position.X < PadLeft || position.X > ActualWidth - PadRight)
            {
                if (_hoverSpeed != null)
                {
                    _hoverSpeed = null;
                    InvalidateVisual();
                }
                return;
            }

            double plotWidth = Math.Max(1.0, ActualWidth - PadLeft - PadRight);
            double fraction = (position.X - PadLeft) / plotWidth;
            _hoverSpeed = fraction * _maxSpeed;
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverSpeed = null;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 60 || height <= 60) return;

            double plotWidth = width - PadLeft - PadRight;
            double plotHeight = height - PadTop - PadBottom;
            var plot = new Rect(PadLeft, PadTop, plotWidth, plotHeight);

            DrawBackground(dc, plot);
            DrawGrid(dc, plot);
            DrawAxes(dc, plot);
            DrawUnityLine(dc, plot);

            if (_points.Count >= 2)
            {
                DrawCurve(dc, plot);
            }
            else
            {
                DrawText(dc, "no curve data", plot.Left + 12, plot.Top + 12, 12, MutedBrush, AxisFace);
            }

            DrawHover(dc, plot);

            if (_hasLive)
            {
                DrawLive(dc, plot);
            }
        }

        // ------------------------------------------------------------------

        private static void DrawBackground(DrawingContext dc, Rect plot)
        {
            var background = new LinearGradientBrush(
                Color.FromRgb(0x10, 0x15, 0x1B),
                Color.FromRgb(0x0C, 0x10, 0x15),
                new Point(0, 0), new Point(0, 1));

            dc.DrawRoundedRectangle(background, null, plot, 8, 8);
        }

        private void DrawGrid(DrawingContext dc, Rect plot)
        {
            double gainStep = ChooseStep(_maxGain, 4);

            for (double gain = 0; gain <= _maxGain + 0.001; gain += gainStep)
            {
                double y = GainToY(gain, plot);
                dc.DrawLine(gain > 0 ? GridPen : AxisPen,
                            new Point(plot.Left, y), new Point(plot.Right, y));

                DrawText(dc, gain.ToString("0.##") + "x", plot.Left - 8, y - 7, 10.5,
                         MutedBrush, LabelFace, TextAlignment.Right);
            }

            double speedStep = ChooseStep(_maxSpeed, 5);

            for (double speed = 0; speed <= _maxSpeed + 0.001; speed += speedStep)
            {
                double x = SpeedToX(speed, plot);
                dc.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));

                DrawText(dc, speed.ToString("0.#"), x, plot.Bottom + 8, 10.5,
                         MutedBrush, LabelFace, TextAlignment.Center);
            }
        }

        private void DrawAxes(DrawingContext dc, Rect plot)
        {
            dc.DrawLine(AxisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
            dc.DrawLine(AxisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

            DrawText(dc, "input speed  (counts / ms)", plot.Left + plot.Width / 2 - 90,
                     plot.Bottom + 10, 11, MutedBrush, AxisFace);

            // Rotated Y axis label.
            dc.PushTransform(new RotateTransform(-90, plot.Left - 34, plot.Top + plot.Height / 2));
            DrawText(dc, "sensitivity", plot.Left - 34, plot.Top + plot.Height / 2 - 7,
                     11, MutedBrush, AxisFace);
            dc.Pop();
        }

        private void DrawUnityLine(DrawingContext dc, Rect plot)
        {
            double y = GainToY(1.0, plot);
            if (y < plot.Top || y > plot.Bottom) return;

            dc.DrawLine(UnityPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, "unity (raw)", plot.Right - 74, y - 14, 10, UnityBrush, LabelFace);
        }

        private void DrawCurve(DrawingContext dc, Rect plot)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(CurvePoint(0, _points[0].Gain, plot), true, true);
                for (int i = 1; i < _points.Count; i++)
                {
                    ctx.LineTo(CurvePoint(_points[i].Speed, _points[i].Gain, plot), true, false);
                }
                ctx.LineTo(new Point(plot.Right, plot.Bottom), false, false);
                ctx.LineTo(new Point(plot.Left, plot.Bottom), false, false);
            }
            geometry.Freeze();

            var fill = new LinearGradientBrush(
                Color.FromArgb(0x55, 0x4F, 0x9C, 0xF9),
                Color.FromArgb(0x05, 0x4F, 0x9C, 0xF9),
                new Point(0, 0), new Point(0, 1));
            dc.DrawGeometry(fill, null, geometry);

            var line = new StreamGeometry();
            using (StreamGeometryContext ctx = line.Open())
            {
                ctx.BeginFigure(CurvePoint(_points[0].Speed, _points[0].Gain, plot), false, false);
                for (int i = 1; i < _points.Count; i++)
                {
                    ctx.LineTo(CurvePoint(_points[i].Speed, _points[i].Gain, plot), true, false);
                }
            }
            line.Freeze();

            dc.DrawGeometry(null, GlowPen, line);
            dc.DrawGeometry(null, CurvePen, line);
        }

        private void DrawHover(DrawingContext dc, Rect plot)
        {
            if (_hoverSpeed == null || _points.Count < 2) return;

            double speed = _hoverSpeed.Value;
            double gain = Interpolate(speed);
            double x = SpeedToX(speed, plot);
            double y = GainToY(gain, plot);

            dc.DrawLine(HoverPen, new Point(x, plot.Top), new Point(x, plot.Bottom));

            dc.DrawEllipse(Brushes.White, null, new Point(x, y), 2.6, 2.6);

            string label = $"{speed:0.0} c/ms  →  {gain:0.00}x";
            DrawText(dc, label, Math.Min(x + 8, plot.Right - 120), Math.Max(y - 22, plot.Top + 4),
                     11, Brushes.White, LabelFace);
        }

        private void DrawLive(DrawingContext dc, Rect plot)
        {
            if (_liveSpeed <= 0) return;

            double x = SpeedToX(Math.Min(_liveSpeed, _maxSpeed), plot);
            double y = GainToY(Math.Min(_liveGain, _maxGain), plot);

            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x33, 0x3F, 0xB9, 0x50)), null,
                           new Point(x, y), 8, 8);
            dc.DrawEllipse(LiveBrush, null, new Point(x, y), 3.4, 3.4);
        }

        // ------------------------------------------------------------------

        private Point CurvePoint(double speed, double gain, Rect plot) =>
            new Point(SpeedToX(speed, plot), GainToY(gain, plot));

        private double SpeedToX(double speed, Rect plot) =>
            plot.Left + Math.Clamp(speed / _maxSpeed, 0, 1) * plot.Width;

        private double GainToY(double gain, Rect plot) =>
            plot.Bottom - Math.Clamp(gain / _maxGain, 0, 1) * plot.Height;

        private double Interpolate(double speed)
        {
            if (_points.Count == 0) return 1.0;
            if (_points.Count == 1) return _points[0].Gain;

            double step = _maxSpeed / (_points.Count - 1);
            int index = (int)Math.Clamp(speed / Math.Max(step, 1e-6), 0, _points.Count - 1);

            if (index >= _points.Count - 1) return _points[_points.Count - 1].Gain;

            double t = (speed - index * step) / step;
            return _points[index].Gain + (_points[index + 1].Gain - _points[index].Gain) * t;
        }

        private static double ChooseStep(double range, int targetTicks)
        {
            double raw = range / Math.Max(1, targetTicks);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(raw, 1e-6))));
            double normalized = raw / magnitude;

            double nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
            return nice * magnitude;
        }

        private void DrawText(DrawingContext dc, string text, double x, double y,
                              double size, Brush brush, Typeface face,
                              TextAlignment alignment = TextAlignment.Left)
        {
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            var formatted = new FormattedText(
                text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                face, size, brush, pixelsPerDip);

            double offset = alignment == TextAlignment.Right
                ? formatted.Width
                : alignment == TextAlignment.Center ? formatted.Width / 2 : 0;

            dc.DrawText(formatted, new Point(x - offset, y));
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(Color color, double thickness, bool dashed = false)
        {
            var pen = new Pen(Frozen(color), thickness);
            if (dashed)
            {
                pen.DashStyle = new DashStyle(new double[] { 4, 4 }, 0);
            }
            pen.Freeze();
            return pen;
        }
    }
}

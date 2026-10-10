using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TailEmg.App.Charts;

/// <summary>
/// Minimal scrolling line chart (right edge = now). Draws directly with DrawingContext so no charting
/// package is needed. Call <see cref="InvalidateVisual"/> after adding points.
/// </summary>
public sealed class RealtimeChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(RealtimeChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YMinProperty = DependencyProperty.Register(
        nameof(YMin), typeof(double), typeof(RealtimeChart),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YMaxProperty = DependencyProperty.Register(
        nameof(YMax), typeof(double), typeof(RealtimeChart),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WindowSecondsProperty = DependencyProperty.Register(
        nameof(WindowSeconds), typeof(double), typeof(RealtimeChart),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface Font = new("Segoe UI");
    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE2)), 1));
    private static readonly Pen FramePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), 1));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)));

    public IReadOnlyList<ChartSeries>? Series { get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public double YMin { get => (double)GetValue(YMinProperty); set => SetValue(YMinProperty, value); }
    public double YMax { get => (double)GetValue(YMaxProperty); set => SetValue(YMaxProperty, value); }
    public double WindowSeconds { get => (double)GetValue(WindowSecondsProperty); set => SetValue(WindowSecondsProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 50)
        {
            return;
        }

        var plot = new Rect(46, 20, w - 46 - 8, h - 20 - 18);
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));

        var yMin = YMin;
        var span = Math.Max(1e-9, YMax - yMin);

        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(i == 0 ? FramePen : GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, (yMin + span * i / 4).ToString("0.##", CultureInfo.InvariantCulture),
                new Point(plot.Left - 4, y - 7), alignRight: true);
        }

        dc.DrawRectangle(null, FramePen, plot);
        DrawText(dc, $"-{WindowSeconds:0}s", new Point(plot.Left, plot.Bottom + 2));
        DrawText(dc, "now", new Point(plot.Right, plot.Bottom + 2), alignRight: true);

        var series = Series;
        if (series is null)
        {
            return;
        }

        var now = ChartClock.Now;
        dc.PushClip(new RectangleGeometry(plot));
        foreach (var s in series)
        {
            var points = s.Points;
            if (points.Count < 2)
            {
                continue;
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var started = false;
                foreach (var (t, v) in points)
                {
                    var x = plot.Right - (now - t) / WindowSeconds * plot.Width;
                    var y = plot.Bottom - Math.Clamp((v - yMin) / span, 0, 1) * plot.Height;
                    if (!started)
                    {
                        ctx.BeginFigure(new Point(x, y), false, false);
                        started = true;
                    }
                    else
                    {
                        ctx.LineTo(new Point(x, y), true, true);
                    }
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, s.Pen, geometry);
        }

        dc.Pop();

        // Legend, laid out left to right above the plot.
        var lx = plot.Left;
        foreach (var s in series)
        {
            var text = MakeText(s.Name, s.Brush);
            dc.DrawRectangle(s.Brush, null, new Rect(lx, 7, 12, 3));
            dc.DrawText(text, new Point(lx + 16, 1));
            lx += 16 + text.Width + 14;
        }
    }

    private void DrawText(DrawingContext dc, string s, Point origin, bool alignRight = false)
    {
        var text = MakeText(s, LabelBrush);
        dc.DrawText(text, alignRight ? new Point(origin.X - text.Width, origin.Y) : origin);
    }

    private FormattedText MakeText(string s, Brush brush) => new(
        s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, 11, brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

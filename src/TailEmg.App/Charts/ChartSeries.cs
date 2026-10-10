using System.Diagnostics;
using System.Windows.Media;

namespace TailEmg.App.Charts;

/// <summary>Monotonic seconds used as the chart's time axis.</summary>
public static class ChartClock
{
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

/// <summary>One line on a chart. Touched from the UI thread only.</summary>
public sealed class ChartSeries
{
    private readonly List<(double T, double V)> _points = new();

    public ChartSeries(string name, Color color, bool dashed = false)
    {
        Name = name;
        Color = color;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Brush = brush;
        var pen = new Pen(brush, 1.5) { LineJoin = PenLineJoin.Round };
        if (dashed)
        {
            pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
        }

        pen.Freeze();
        Pen = pen;
    }

    public string Name { get; }
    public Color Color { get; }
    public Brush Brush { get; }
    public Pen Pen { get; }
    public IReadOnlyList<(double T, double V)> Points => _points;

    public void Add(double t, double value, double windowSeconds)
    {
        _points.Add((t, value));
        var cutoff = t - windowSeconds - 1;
        var drop = 0;
        while (drop < _points.Count && _points[drop].T < cutoff)
        {
            drop++;
        }

        if (drop > 0)
        {
            _points.RemoveRange(0, drop);
        }
    }

    public void Clear() => _points.Clear();
}

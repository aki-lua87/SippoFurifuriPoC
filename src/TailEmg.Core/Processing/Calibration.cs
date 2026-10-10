namespace TailEmg.Core.Processing;

public enum CalibrationKind
{
    Rest,
    Max,
}

public static class CalibrationMath
{
    /// <summary>Percentile (0..1) with linear interpolation. Returns NaN for an empty input.</summary>
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var position = Math.Clamp(p, 0, 1) * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    public static double Median(IReadOnlyList<double> values) => Percentile(values, 0.5);
}

/// <summary>
/// Collects raw values for a fixed duration (measured on sample timestamps) and reduces them to one number:
/// REST → median (robust to blips), MAX → 95th percentile (a single spike does not become MAX).
/// Thread-safe: samples arrive on the input thread, the UI reads progress.
/// </summary>
public sealed class CalibrationCollector
{
    private readonly object _gate = new();
    private readonly List<double> _values = new();
    private readonly TimeSpan _duration;
    private readonly double _maxPercentile;
    private DateTimeOffset? _start;
    private DateTimeOffset _last;
    private double? _result;
    private bool _complete;

    public CalibrationCollector(CalibrationKind kind, TimeSpan duration, double maxPercentile = 0.95)
    {
        Kind = kind;
        _duration = duration;
        _maxPercentile = maxPercentile;
    }

    public CalibrationKind Kind { get; }

    public bool IsComplete { get { lock (_gate) { return _complete; } } }

    public double? Result { get { lock (_gate) { return _result; } } }

    /// <summary>0..1 of the collection window covered so far.</summary>
    public double Progress
    {
        get
        {
            lock (_gate)
            {
                if (_complete)
                {
                    return 1;
                }

                return _start is null ? 0 : Math.Clamp((_last - _start.Value) / _duration, 0, 1);
            }
        }
    }

    /// <summary>Adds a sample; returns true on the call that completes the collection.</summary>
    public bool Add(DateTimeOffset timestamp, double value)
    {
        lock (_gate)
        {
            if (_complete || !double.IsFinite(value))
            {
                return false;
            }

            _start ??= timestamp;
            _last = timestamp;
            _values.Add(value);
            if (timestamp - _start.Value < _duration)
            {
                return false;
            }

            _result = Kind == CalibrationKind.Rest
                ? CalibrationMath.Median(_values)
                : CalibrationMath.Percentile(_values, _maxPercentile);
            _complete = true;
            return true;
        }
    }
}

using TailEmg.Core.Processing;

namespace TailEmg.Core.Tests;

public class CalibrationTests
{
    [Fact]
    public void Median_IgnoresOutliers()
        => Assert.Equal(10, CalibrationMath.Median(new double[] { 10, 11, 9, 10, 5000, 10, 9 }));

    [Fact]
    public void Percentile_InterpolatesAndClamps()
    {
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
        Assert.Equal(95.05, CalibrationMath.Percentile(values, 0.95), 9);
        Assert.Equal(1, CalibrationMath.Percentile(values, -1));
        Assert.Equal(100, CalibrationMath.Percentile(values, 2));
        Assert.True(double.IsNaN(CalibrationMath.Percentile(Array.Empty<double>(), 0.5)));
    }

    [Fact]
    public void Rest_CompletesAfterDuration_WithMedian()
    {
        var c = new CalibrationCollector(CalibrationKind.Rest, TimeSpan.FromSeconds(3));
        var t0 = DateTimeOffset.UtcNow;
        var completed = false;
        for (var ms = 0; ms <= 3000 && !completed; ms += 10)
        {
            // one spike must not matter
            completed = c.Add(t0.AddMilliseconds(ms), ms == 500 ? 4000 : 20);
        }

        Assert.True(completed);
        Assert.True(c.IsComplete);
        Assert.Equal(20, c.Result);
        Assert.Equal(1, c.Progress);
    }

    [Fact]
    public void Max_UsesNinetyFifthPercentile_NotTheSpike()
    {
        var c = new CalibrationCollector(CalibrationKind.Max, TimeSpan.FromSeconds(1));
        var t0 = DateTimeOffset.UtcNow;
        for (var i = 0; i <= 100; i++)
        {
            c.Add(t0.AddMilliseconds(i * 10), i == 50 ? 4095 : 2000 + i);
        }

        Assert.True(c.IsComplete);
        Assert.InRange(c.Result!.Value, 2000, 2200); // not 4095
    }

    [Fact]
    public void NotComplete_BeforeDuration_AndIgnoresNonFinite()
    {
        var c = new CalibrationCollector(CalibrationKind.Rest, TimeSpan.FromSeconds(3));
        var t0 = DateTimeOffset.UtcNow;
        c.Add(t0, 10);
        c.Add(t0.AddSeconds(1), double.NaN);
        Assert.False(c.IsComplete);
        Assert.Null(c.Result);
        Assert.InRange(c.Progress, 0, 0.01); // NaN sample did not advance the window
    }

    [Fact]
    public void AfterComplete_FurtherSamplesAreIgnored()
    {
        var c = new CalibrationCollector(CalibrationKind.Rest, TimeSpan.FromMilliseconds(100));
        var t0 = DateTimeOffset.UtcNow;
        c.Add(t0, 5);
        c.Add(t0.AddMilliseconds(100), 5);
        c.Add(t0.AddMilliseconds(200), 9999);
        Assert.Equal(5, c.Result);
    }
}

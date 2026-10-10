using TailEmg.Core.Processing;

namespace TailEmg.Core.Tests;

public class SignalProcessingTests
{
    [Theory]
    [InlineData(200, 0)]
    [InlineData(1200, 0.5)]
    [InlineData(2200, 1)]
    [InlineData(0, 0)]      // below rest clamps
    [InlineData(4095, 1)]   // above max clamps
    public void Normalize_MapsRestToMax(double input, double expected)
        => Assert.Equal(expected, SignalProcessing.Normalize(input, 200, 2200), 9);

    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(1000, 500)]
    public void Normalize_InvalidCalibration_ReturnsZero(double rest, double max)
        => Assert.Equal(0, SignalProcessing.Normalize(700, rest, max));

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.05, 0.0)]
    [InlineData(0.03, 0.0)]
    public void Deadzone_AtOrBelow_IsZero(double input, double expected)
        => Assert.Equal(expected, SignalProcessing.ApplyDeadzone(input, 0.05));

    [Fact]
    public void Deadzone_Above_RemapsToUnitRange()
    {
        Assert.Equal(1.0, SignalProcessing.ApplyDeadzone(1.0, 0.05), 9);
        Assert.Equal(0.5, SignalProcessing.ApplyDeadzone(0.525, 0.05), 9);
        Assert.True(SignalProcessing.ApplyDeadzone(0.06, 0.05) > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(1)]
    public void Gamma_One_IsIdentity(double input)
        => Assert.Equal(input, SignalProcessing.ApplyCurve(input, ResponseCurveType.Gamma, 1.0), 9);

    [Fact]
    public void Gamma_Two_Squares()
        => Assert.Equal(0.25, SignalProcessing.ApplyCurve(0.5, ResponseCurveType.Gamma, 2.0), 9);

    [Fact]
    public void Ema_KnownSequence()
    {
        var y = 0.0;
        y = SignalProcessing.Ema(y, 1.0, 0.25);
        Assert.Equal(0.25, y, 9);
        y = SignalProcessing.Ema(y, 1.0, 0.25);
        Assert.Equal(0.4375, y, 9);
        y = SignalProcessing.Ema(y, 0.0, 0.25);
        Assert.Equal(0.328125, y, 9);
    }

    [Fact]
    public void EmaTimed_OneReferenceStep_EqualsPlainEma()
        => Assert.Equal(
            SignalProcessing.Ema(0, 1, 0.25),
            SignalProcessing.EmaTimed(0, 1, 0.25, SignalProcessing.EmaReferenceSeconds),
            9);

    [Fact]
    public void EmaTimed_ZeroDt_LeavesValueUnchanged()
        => Assert.Equal(0.3, SignalProcessing.EmaTimed(0.3, 1.0, 0.25, 0), 9);

    [Fact]
    public void EmaTimed_SameWallTime_SameResultRegardlessOfSampleRate()
    {
        // 1 reference step delivered as 1 sample vs. 20 samples must land in the same place.
        var one = SignalProcessing.EmaTimed(0, 1, 0.25, SignalProcessing.EmaReferenceSeconds);
        var many = 0.0;
        for (var i = 0; i < 20; i++)
        {
            many = SignalProcessing.EmaTimed(many, 1, 0.25, SignalProcessing.EmaReferenceSeconds / 20);
        }

        Assert.Equal(one, many, 9);
    }

    [Fact]
    public void Pipeline_Smoothing_IsIndependentOfInputRate()
    {
        double Run(int sampleIntervalMs)
        {
            var pipeline = new EmgPipeline(new ProcessingSettings());
            var t0 = DateTimeOffset.UtcNow;
            ProcessedSample? last = null;
            for (var ms = 0; ms <= 100; ms += sampleIntervalMs)
            {
                last = pipeline.Process(new(t0.AddMilliseconds(ms), 0, 4095));
            }

            return last!.Filtered;
        }

        // The first sample after Reset counts as one reference step, so allow that small offset.
        Assert.Equal(Run(2), Run(20), 1);
    }

    [Fact]
    public void Pipeline_FullScaleInput_ConvergesToOne()
    {
        var pipeline = new EmgPipeline(new ProcessingSettings());
        var t0 = DateTimeOffset.UtcNow;
        ProcessedSample? last = null;
        for (var i = 0; i < 100; i++)
        {
            last = pipeline.Process(new(t0.AddMilliseconds(33 * i), 0, 4095));
        }

        Assert.NotNull(last);
        Assert.Equal(1.0, last!.Normalized, 9);
        Assert.Equal(1.0, last.Filtered, 3);
    }
}

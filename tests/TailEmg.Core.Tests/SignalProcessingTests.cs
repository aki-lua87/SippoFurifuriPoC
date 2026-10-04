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
    public void Pipeline_FullScaleInput_ConvergesToOne()
    {
        var pipeline = new EmgPipeline(new ProcessingSettings());
        ProcessedSample? last = null;
        for (var i = 0; i < 100; i++)
        {
            last = pipeline.Process(new(DateTimeOffset.UtcNow, 0, 4095));
        }

        Assert.NotNull(last);
        Assert.Equal(1.0, last!.Normalized, 9);
        Assert.Equal(1.0, last.Filtered, 3);
    }
}

using TailEmg.Core.Input;

namespace TailEmg.Core.Tests;

public class FakeEmgInputTests
{
    [Fact]
    public void SineValue_GoesZeroToMaxToZero()
    {
        Assert.Equal(0, FakeEmgInput.SineValue(0, 4), 6);
        Assert.Equal(4095, FakeEmgInput.SineValue(2, 4), 6);
        Assert.Equal(0, FakeEmgInput.SineValue(4, 4), 6);
    }

    [Fact]
    public async Task Manual_EmitsSliderValue_AndStops()
    {
        var input = new FakeEmgInput { Mode = FakeEmgMode.Manual, ManualValue = 1234 };
        var got = new TaskCompletionSource<EmgSample>(TaskCreationOptions.RunContinuationsAsynchronously);
        input.SampleReceived += (_, s) => got.TrySetResult(s);

        await input.StartAsync(CancellationToken.None);
        var sample = await got.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await input.StopAsync();

        Assert.Equal(1234, sample.Value);
        Assert.Equal(0, sample.Channel);
        Assert.False(input.IsRunning);
    }
}

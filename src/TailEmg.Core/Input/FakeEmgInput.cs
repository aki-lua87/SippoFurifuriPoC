namespace TailEmg.Core.Input;

public enum FakeEmgMode
{
    Manual,
    Sine,
}

/// <summary>
/// Hardware-free EMG source. Manual: value comes from <see cref="ManualValue"/> (GUI slider).
/// Sine: 0 → 4095 → 0 every <see cref="SinePeriodSeconds"/>.
/// </summary>
public sealed class FakeEmgInput : IEmgInput
{
    public const double AdcMax = 4095;
    public const int DefaultChannel = 0;

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20); // 50 Hz, like a BLE stream

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile int _mode = (int)FakeEmgMode.Manual;
    private double _manualValue;
    private double _sinePeriodSeconds = 4.0;

    public event EventHandler<EmgSample>? SampleReceived;

    public FakeEmgMode Mode
    {
        get => (FakeEmgMode)_mode;
        set => _mode = (int)value;
    }

    public double ManualValue
    {
        get => Volatile.Read(ref _manualValue);
        set => Volatile.Write(ref _manualValue, Math.Clamp(value, 0, AdcMax));
    }

    public double SinePeriodSeconds
    {
        get => Volatile.Read(ref _sinePeriodSeconds);
        set => Volatile.Write(ref _sinePeriodSeconds, Math.Max(0.1, value));
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;
        if (cts is null || loop is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    public static double SineValue(double elapsedSeconds, double periodSeconds)
        => AdcMax * (1 - Math.Cos(2 * Math.PI * elapsedSeconds / periodSeconds)) / 2;

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        var start = DateTimeOffset.UtcNow;
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var now = DateTimeOffset.UtcNow;
                var value = Mode == FakeEmgMode.Sine
                    ? SineValue((now - start).TotalSeconds, SinePeriodSeconds)
                    : ManualValue;
                SampleReceived?.Invoke(this, new EmgSample(now, DefaultChannel, value));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}

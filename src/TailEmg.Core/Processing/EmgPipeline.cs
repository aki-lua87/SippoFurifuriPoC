using System.Collections.Concurrent;
using TailEmg.Core.Input;

namespace TailEmg.Core.Processing;

/// <summary>Live-tunable processing parameters. Written by UI, read by the sample thread.</summary>
public sealed class ProcessingSettings
{
    private double _rest = 0;
    private double _max = FakeEmgInput.AdcMax;
    private double _deadzone = 0.05;
    private double _gamma = 1.0;
    private double _alpha = 0.25;
    private int _curve = (int)ResponseCurveType.Linear;

    public double Rest { get => Volatile.Read(ref _rest); set => Volatile.Write(ref _rest, value); }
    public double Max { get => Volatile.Read(ref _max); set => Volatile.Write(ref _max, value); }
    public double Deadzone { get => Volatile.Read(ref _deadzone); set => Volatile.Write(ref _deadzone, value); }
    public double Gamma { get => Volatile.Read(ref _gamma); set => Volatile.Write(ref _gamma, value); }
    public double SmoothingAlpha { get => Volatile.Read(ref _alpha); set => Volatile.Write(ref _alpha, value); }

    public ResponseCurveType Curve
    {
        get => (ResponseCurveType)Volatile.Read(ref _curve);
        set => Volatile.Write(ref _curve, (int)value);
    }
}

public sealed record ProcessedSample(
    DateTimeOffset Timestamp,
    int Channel,
    double Raw,
    double Normalized,
    double DeadzoneOutput,
    double CurveOutput,
    double Filtered);

/// <summary>
/// Raw → Normalize → Deadzone → Curve → EMA (per channel). Pure function of sample + settings,
/// no knowledge of BLE, OSC or UI. The latest value per channel is kept for the OSC sender to poll.
/// </summary>
public sealed class EmgPipeline
{
    private readonly ConcurrentDictionary<int, double> _filtered = new();
    private readonly ConcurrentDictionary<int, ProcessedSample> _latest = new();

    public EmgPipeline(ProcessingSettings settings) => Settings = settings;

    public ProcessingSettings Settings { get; }

    public event EventHandler<ProcessedSample>? SampleProcessed;

    public void OnSample(object? sender, EmgSample sample) => Process(sample);

    public ProcessedSample Process(EmgSample sample)
    {
        var s = Settings;
        var normalized = SignalProcessing.Normalize(sample.Value, s.Rest, s.Max);
        var dead = SignalProcessing.ApplyDeadzone(normalized, s.Deadzone);
        var curved = SignalProcessing.ApplyCurve(dead, s.Curve, s.Gamma);

        // Channels are only ever processed from one producer thread each, so read-modify-write is safe.
        var previous = _filtered.GetValueOrDefault(sample.Channel, 0);
        var filtered = SignalProcessing.Ema(previous, curved, s.SmoothingAlpha);
        _filtered[sample.Channel] = filtered;

        var result = new ProcessedSample(sample.Timestamp, sample.Channel, sample.Value, normalized, dead, curved, filtered);
        _latest[sample.Channel] = result;
        SampleProcessed?.Invoke(this, result);
        return result;
    }

    public ProcessedSample? GetLatest(int channel) => _latest.GetValueOrDefault(channel);

    /// <summary>Latest filtered value for the channel (0 if nothing has been processed yet).</summary>
    public double GetFiltered(int channel) => _filtered.GetValueOrDefault(channel, 0);

    public void Reset()
    {
        _filtered.Clear();
        _latest.Clear();
    }
}

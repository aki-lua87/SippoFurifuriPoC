namespace TailEmg.Core.Input;

/// <summary>One raw EMG reading. Channel is kept so 2ch+ can be added later.</summary>
public sealed record EmgSample(
    DateTimeOffset Timestamp,
    int Channel,
    double Value)
{
    /// <summary>Original transport text (e.g. the BLE payload "684.00"), kept for logging. Null for synthetic inputs.</summary>
    public string? RawText { get; init; }
}

public interface IEmgInput
{
    event EventHandler<EmgSample>? SampleReceived;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

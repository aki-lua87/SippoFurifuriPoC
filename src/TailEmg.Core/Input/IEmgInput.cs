namespace TailEmg.Core.Input;

/// <summary>One raw EMG reading. Channel is kept so 2ch+ can be added later.</summary>
public sealed record EmgSample(
    DateTimeOffset Timestamp,
    int Channel,
    double Value);

public interface IEmgInput
{
    event EventHandler<EmgSample>? SampleReceived;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

using System.Net.Sockets;

namespace TailEmg.Core.Osc;

/// <summary>
/// Sends the latest value from <c>valueSource</c> at a fixed rate, independent of how often
/// samples arrive. Value is clamped to 0..1. Supports a "safe state" that ramps to 0.
/// </summary>
public sealed class OscPublisher : IAsyncDisposable
{
    private static readonly TimeSpan SafeRampDuration = TimeSpan.FromMilliseconds(200);

    private readonly OscSettings _settings;
    private readonly Func<double> _valueSource;
    private readonly object _gate = new();

    private UdpClient? _udp;
    private string? _udpTarget;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _safeState;
    private double _lastSent;

    public OscPublisher(OscSettings settings, Func<double> valueSource)
    {
        _settings = settings;
        _valueSource = valueSource;
    }

    /// <summary>The value most recently handed to the socket (or that would have been, if disabled).</summary>
    public double LastOutput => Volatile.Read(ref _lastSent);

    /// <summary>Last send error, null if the previous send succeeded.</summary>
    public string? LastError { get; private set; }

    /// <summary>When true the output ramps to 0 over ~200 ms instead of following the source (e.g. input disconnected).</summary>
    public bool SafeState { get => _safeState; set => _safeState = value; }

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = Task.Run(() => RunAsync(ct));
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;
        if (cts is not null)
        {
            cts.Cancel();
            try
            {
                if (loop is not null)
                {
                    await loop.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }

            cts.Dispose();
        }

        // Leave the avatar in a neutral pose.
        if (_settings.Enabled)
        {
            Send(0);
        }

        Volatile.Write(ref _lastSent, 0);
        CloseSocket();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(1.0 / Math.Clamp(_settings.RateHz, 1, 240));
        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var value = ComputeOutput(period);
                Volatile.Write(ref _lastSent, value);
                if (_settings.Enabled)
                {
                    Send(value);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private double ComputeOutput(TimeSpan dt)
    {
        if (_safeState)
        {
            var step = dt.TotalSeconds / SafeRampDuration.TotalSeconds;
            return Math.Max(0, Volatile.Read(ref _lastSent) - step);
        }

        var v = _valueSource();
        return double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);
    }

    private void Send(double value)
    {
        try
        {
            var packet = OscPacket.BuildFloat(_settings.Address, (float)Math.Clamp(value, 0, 1));
            UdpClient udp;
            lock (_gate)
            {
                var target = $"{_settings.Host}:{_settings.Port}";
                if (_udp is null || _udpTarget != target)
                {
                    _udp?.Dispose();
                    _udp = new UdpClient();
                    _udp.Connect(_settings.Host, _settings.Port);
                    _udpTarget = target;
                }

                udp = _udp;
            }

            udp.Send(packet, packet.Length);
            LastError = null;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or ArgumentException)
        {
            LastError = ex.Message;
        }
    }

    private void CloseSocket()
    {
        lock (_gate)
        {
            _udp?.Dispose();
            _udp = null;
            _udpTarget = null;
        }
    }
}

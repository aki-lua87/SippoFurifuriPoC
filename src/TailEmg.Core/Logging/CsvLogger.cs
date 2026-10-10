using System.Globalization;
using System.Text;
using System.Threading.Channels;
using TailEmg.Core.Ble;

namespace TailEmg.Core.Logging;

public sealed record CsvLogRow(
    DateTimeOffset Timestamp,
    string? BleRawText,
    double RawValue,
    double Normalized,
    double DeadzoneOutput,
    double CurveOutput,
    double FilteredOutput,
    double OscOutput,
    string ConnectionState);

/// <summary>
/// CSV writer on its own thread. <see cref="TryWrite"/> only enqueues into a bounded channel and never blocks,
/// so the BLE notification thread and the OSC loop are not slowed by disk IO. If the disk cannot keep up,
/// rows are dropped (and counted) rather than stalling the producer.
/// </summary>
public sealed class CsvLogger
{
    public const string Header =
        "Timestamp,BleRawText,RawValue,Normalized,DeadzoneOutput,CurveOutput,FilteredOutput,OscOutput,ConnectionState";

    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private Channel<CsvLogRow>? _channel;
    private Task? _writer;
    private long _rowsWritten;
    private long _dropped;

    public bool IsRunning => _writer is { IsCompleted: false };
    public string? FilePath { get; private set; }
    public long RowsWritten => Interlocked.Read(ref _rowsWritten);
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>First IO error, if any. After an error the logger discards rows until stopped.</summary>
    public string? LastError { get; private set; }

    public void Start(string path)
    {
        if (IsRunning)
        {
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Open here so a bad path fails at Start (to the caller), not silently in the background.
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(Header);

        FilePath = path;
        LastError = null;
        Interlocked.Exchange(ref _rowsWritten, 0);
        Interlocked.Exchange(ref _dropped, 0);
        _channel = Channel.CreateBounded<CsvLogRow>(new BoundedChannelOptions(20_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });
        var reader = _channel.Reader;
        _writer = Task.Run(() => WriteLoopAsync(reader, writer));
    }

    public bool TryWrite(CsvLogRow row)
    {
        var channel = _channel;
        if (channel is null)
        {
            return false;
        }

        if (channel.Writer.TryWrite(row))
        {
            return true;
        }

        Interlocked.Increment(ref _dropped);
        return false;
    }

    public async Task StopAsync()
    {
        var channel = _channel;
        var writer = _writer;
        _channel = null;
        _writer = null;
        channel?.Writer.TryComplete();
        if (writer is not null)
        {
            await writer.ConfigureAwait(false);
        }
    }

    public static string FormatRow(CsvLogRow r)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Join(',',
            r.Timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", inv),
            Escape(r.BleRawText is null ? "" : BlePayloadParser.ToPrintable(r.BleRawText)),
            r.RawValue.ToString("0.00", inv),
            r.Normalized.ToString("0.0000", inv),
            r.DeadzoneOutput.ToString("0.0000", inv),
            r.CurveOutput.ToString("0.0000", inv),
            r.FilteredOutput.ToString("0.0000", inv),
            r.OscOutput.ToString("0.0000", inv),
            Escape(r.ConnectionState));
    }

    private static string Escape(string s)
        => s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private async Task WriteLoopAsync(ChannelReader<CsvLogRow> reader, StreamWriter writer)
    {
        var lastFlush = DateTime.UtcNow;
        var failed = false;
        try
        {
            await foreach (var row in reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (failed)
                {
                    continue; // drain so producers are never blocked
                }

                try
                {
                    writer.WriteLine(FormatRow(row));
                    Interlocked.Increment(ref _rowsWritten);
                    if (DateTime.UtcNow - lastFlush >= FlushInterval)
                    {
                        writer.Flush();
                        lastFlush = DateTime.UtcNow;
                    }
                }
                catch (IOException ex)
                {
                    LastError = ex.Message;
                    failed = true;
                }
            }
        }
        finally
        {
            try
            {
                writer.Dispose(); // flushes
            }
            catch (IOException ex)
            {
                LastError ??= ex.Message;
            }
        }
    }
}

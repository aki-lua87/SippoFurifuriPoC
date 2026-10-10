using TailEmg.Core.Logging;

namespace TailEmg.Core.Tests;

public class CsvLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tailemg-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static CsvLogRow Row(string? text = "812.00") => new(
        new DateTimeOffset(2026, 10, 4, 23, 45, 1, 123, TimeSpan.FromHours(9)),
        text, 812, 0.31, 0.27, 0.27, 0.24, 0.24, "Connected");

    [Fact]
    public void FormatRow_MatchesSpecExample()
        => Assert.Equal(
            "2026-10-04T23:45:01.123+09:00,812.00,812.00,0.3100,0.2700,0.2700,0.2400,0.2400,Connected",
            CsvLogger.FormatRow(Row()));

    [Fact]
    public void FormatRow_EscapesAndMakesControlCharsVisible()
    {
        Assert.Contains(",\"1,2\",", CsvLogger.FormatRow(Row("1,2")));
        Assert.Contains(",12\\x00,", CsvLogger.FormatRow(Row("12\0")));
        Assert.Contains("+09:00,,812.00", CsvLogger.FormatRow(Row(null)));
    }

    [Fact]
    public async Task WritesHeaderAndAllRows_WithoutBlockingProducer()
    {
        var path = Path.Combine(_dir, "sub", "log.csv");
        var logger = new CsvLogger();
        logger.Start(path);
        Assert.True(logger.IsRunning);

        for (var i = 0; i < 1000; i++)
        {
            Assert.True(logger.TryWrite(Row()));
        }

        await logger.StopAsync();

        var lines = File.ReadAllLines(path);
        Assert.Equal(CsvLogger.Header, lines[0]);
        Assert.Equal(1001, lines.Length);
        Assert.Equal(1000, logger.RowsWritten);
        Assert.Equal(0, logger.Dropped);
        Assert.False(logger.IsRunning);
    }

    [Fact]
    public void TryWrite_WhenNotStarted_ReturnsFalse()
        => Assert.False(new CsvLogger().TryWrite(Row()));
}

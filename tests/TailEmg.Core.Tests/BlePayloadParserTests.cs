using System.Text;
using TailEmg.Core.Ble;

namespace TailEmg.Core.Tests;

public class BlePayloadParserTests
{
    [Theory]
    [InlineData("123", 123)]
    [InlineData("123.0", 123)]
    [InlineData("123.00", 123)]
    [InlineData(" 123.00 ", 123)]
    [InlineData("4095.00", 4095)]
    [InlineData("0.00", 0)]
    [InlineData("684.00\0", 684)]
    public void Valid_Parses(string text, double expected)
    {
        Assert.True(BlePayloadParser.TryParse(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("foo")]
    [InlineData("1,234")]
    [InlineData("\0")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("12 34")]
    public void Invalid_ReturnsFalse_WithoutThrowing(string? text)
    {
        Assert.False(BlePayloadParser.TryParse(text, out var value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void Bytes_ParseAsUtf8()
    {
        var bytes = Encoding.ASCII.GetBytes("684.00");
        Assert.True(BlePayloadParser.TryParse(bytes, out var text, out var value));
        Assert.Equal("684.00", text);
        Assert.Equal(684, value);
    }

    [Fact]
    public void Bytes_InvalidUtf8_DoesNotThrow()
    {
        Assert.False(BlePayloadParser.TryParse(new byte[] { 0xFF, 0xFE, 0x00 }, out _, out _));
        Assert.False(BlePayloadParser.TryParse(ReadOnlySpan<byte>.Empty, out _, out _));
    }

    [Fact]
    public void Hex_And_Printable()
    {
        Assert.Equal("36 38 34 2E 30 30", BlePayloadParser.ToHex(Encoding.ASCII.GetBytes("684.00")));
        Assert.Equal("", BlePayloadParser.ToHex(ReadOnlySpan<byte>.Empty));
        Assert.Equal("1\\x00", BlePayloadParser.ToPrintable("1\0"));
    }

    [Fact]
    public void Notification_LogLine_ShowsAllForms()
    {
        var bytes = Encoding.ASCII.GetBytes("684.00");
        var line = new BleNotification(DateTimeOffset.Now, bytes, "684.00", 684).ToLogLine();
        Assert.Contains("len=6", line);
        Assert.Contains("36 38 34 2E 30 30", line);
        Assert.Contains("\"684.00\"", line);
        Assert.Contains("-> 684", line);
    }
}

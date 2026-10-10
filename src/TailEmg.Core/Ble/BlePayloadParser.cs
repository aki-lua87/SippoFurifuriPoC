using System.Globalization;
using System.Text;

namespace TailEmg.Core.Ble;

/// <summary>MyoWare payload: ASCII/UTF-8 decimal string such as "684.00". Never throws.</summary>
public static class BlePayloadParser
{
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // Whitespace, and stray NULs some stacks leave at the end of a string characteristic.
        var trimmed = text.Trim().TrimEnd('\0').Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Decodes bytes as UTF-8 (invalid sequences become U+FFFD) and parses. <paramref name="text"/> is always set.</summary>
    public static bool TryParse(ReadOnlySpan<byte> bytes, out string text, out double value)
    {
        text = Encoding.UTF8.GetString(bytes);
        return TryParse(text, out value);
    }

    public static string ToHex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).Length == 0
        ? ""
        : string.Join(' ', Convert.ToHexString(bytes).Chunk(2).Select(c => new string(c)));

    /// <summary>Makes control characters visible (\0 → \x00) so raw payloads can be read in a log.</summary>
    public static string ToPrintable(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsControl(c))
            {
                sb.Append("\\x").Append(((int)c).ToString("X2"));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

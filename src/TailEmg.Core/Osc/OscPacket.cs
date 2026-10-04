using System.Buffers.Binary;
using System.Text;

namespace TailEmg.Core.Osc;

/// <summary>Minimal OSC 1.0 encoder: address + ",f" + big-endian float32.</summary>
public static class OscPacket
{
    public static byte[] BuildFloat(string address, float value)
    {
        var addr = PaddedString(address);
        var tags = PaddedString(",f");
        var buffer = new byte[addr.Length + tags.Length + 4];
        addr.CopyTo(buffer, 0);
        tags.CopyTo(buffer, addr.Length);
        BinaryPrimitives.WriteSingleBigEndian(buffer.AsSpan(addr.Length + tags.Length), value);
        return buffer;
    }

    /// <summary>ASCII string, NUL-terminated, padded with NULs to a multiple of 4 bytes.</summary>
    private static byte[] PaddedString(string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        var length = (bytes.Length / 4 + 1) * 4; // always at least one NUL terminator
        var result = new byte[length];
        bytes.CopyTo(result, 0);
        return result;
    }
}

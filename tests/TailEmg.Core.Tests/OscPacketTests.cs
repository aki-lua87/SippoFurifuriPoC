using System.Text;
using TailEmg.Core.Osc;

namespace TailEmg.Core.Tests;

public class OscPacketTests
{
    [Fact]
    public void BuildFloat_ProducesSpecCompliantPacket()
    {
        const string address = "/avatar/parameters/TailEMG"; // 26 chars → padded to 28
        var packet = OscPacket.BuildFloat(address, 0.5f);

        Assert.Equal(28 + 4 + 4, packet.Length);
        Assert.Equal(0, packet.Length % 4);

        Assert.Equal(address, Encoding.ASCII.GetString(packet, 0, 26));
        Assert.Equal(new byte[] { 0, 0 }, packet[26..28]);

        Assert.Equal(new byte[] { (byte)',', (byte)'f', 0, 0 }, packet[28..32]);

        // 0.5f = 0x3F000000 big-endian
        Assert.Equal(new byte[] { 0x3F, 0x00, 0x00, 0x00 }, packet[32..36]);
    }

    [Fact]
    public void BuildFloat_AddressOfExactMultipleOfFour_GetsFourNulBytes()
    {
        var packet = OscPacket.BuildFloat("/abc", 1.0f); // 4 chars → 8 bytes with terminator
        Assert.Equal(8 + 4 + 4, packet.Length);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, packet[4..8]);
        Assert.Equal(new byte[] { 0x3F, 0x80, 0x00, 0x00 }, packet[12..16]);
    }
}

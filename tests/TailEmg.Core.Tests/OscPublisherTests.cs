using System.Net;
using System.Net.Sockets;
using TailEmg.Core.Osc;

namespace TailEmg.Core.Tests;

public class OscPublisherTests
{
    [Fact]
    public async Task Publisher_SendsValueOverUdp_AndFinalZeroOnStop()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
        var settings = new OscSettings { Host = "127.0.0.1", Port = port, RateHz = 60 };

        var publisher = new OscPublisher(settings, () => 0.75);
        publisher.Start();

        var first = await listener.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(OscPacket.BuildFloat(settings.Address, 0.75f), first.Buffer);

        await publisher.StopAsync();

        // Drain; the last datagram must be the 0.0 sent on stop.
        byte[] last = first.Buffer;
        while (true)
        {
            try
            {
                last = (await listener.ReceiveAsync().WaitAsync(TimeSpan.FromMilliseconds(200))).Buffer;
            }
            catch (TimeoutException)
            {
                break;
            }
        }

        Assert.Equal(OscPacket.BuildFloat(settings.Address, 0f), last);
    }
}

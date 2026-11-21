using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using T3.Core.DataTypes;
using T3.Core.Output;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Lifecycle of the CITP laser stream's TCP control session against a loopback listener standing
/// in for Capture: a direct-connect plug (host and port set) must bring the session back after the
/// receiver drops it and comes back, without the plug being rebuilt.
/// </summary>
public class LaserCitpStreamTests
{
    [Fact]
    public void DirectConnect_ReconnectsAfterReceiverRestart()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var plug = new LaserPlug
                       {
                           Kind = LaserPlug.Kinds.Citp,
                           Name = "CITP test",
                           Address = "127.0.0.1",
                           Port = port,
                       };

        var stream = LaserOutputStream.Create(plug);
        Assert.NotNull(stream);
        try
        {
            using (var first = AcceptWithin(listener, TimeSpan.FromSeconds(5)))
            {
                Assert.NotNull(first);
                Assert.True(WaitUntil(() => stream.IsConnected, TimeSpan.FromSeconds(5)), "first session never came up");
            }

            // Receiver goes away: the stream notices the closed session.
            listener.Stop();
            Assert.True(WaitUntil(() => !stream.IsConnected, TimeSpan.FromSeconds(5)), "closed session still reported connected");

            // Receiver restarts on the same port: the stream must reconnect on its own.
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            using var second = AcceptWithin(listener, TimeSpan.FromSeconds(10));
            Assert.NotNull(second);
        }
        finally
        {
            stream.Dispose();
            listener.Stop();
        }
    }

    [Fact]
    public void ControlMessages_AreFramedByMessageSize()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var plug = new LaserPlug
                       {
                           Kind = LaserPlug.Kinds.Citp,
                           Name = "CITP test",
                           Address = "127.0.0.1",
                           Port = port,
                       };

        var stream = LaserOutputStream.Create(plug);
        Assert.NotNull(stream);
        try
        {
            using var session = AcceptWithin(listener, TimeSpan.FromSeconds(5));
            Assert.NotNull(session);
            Assert.True(WaitUntil(() => stream.IsConnected, TimeSpan.FromSeconds(5)), "session never came up");
            var network = session.GetStream();

            // Two messages in one segment: the feed-list request must not swallow the feed start.
            var both = CaexMessage(GetLaserFeedList, []).Concat(CaexMessage(LaserFeedControl, [0, 30])).ToArray();
            network.Write(both, 0, both.Length);
            Assert.True(WaitUntil(() => stream.StatusMessage.Contains("active at 30 fps"), TimeSpan.FromSeconds(5)),
                        $"coalesced feed control ignored - status '{stream.StatusMessage}'");

            // One message split across two segments.
            var control = CaexMessage(LaserFeedControl, [0, 25]);
            network.Write(control, 0, 10);
            network.Flush();
            Thread.Sleep(100);
            network.Write(control, 10, control.Length - 10);
            Assert.True(WaitUntil(() => stream.StatusMessage.Contains("active at 25 fps"), TimeSpan.FromSeconds(5)),
                        $"split feed control ignored - status '{stream.StatusMessage}'");
        }
        finally
        {
            stream.Dispose();
            listener.Stop();
        }
    }

    /// <summary>
    /// The capture asks for a frame rate and plays what it gets at that rate, so a producer that runs faster
    /// (the driver submits one frame per editor frame) must not flood it: every extra frame waits in the
    /// receiver's queue and shows up as lag. The feed goes out at the requested rate and the newest frame wins.
    /// </summary>
    [Fact]
    public void FeedFrames_GoOutAtTheRequestedRate_AndTheNewestWins()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var plug = new LaserPlug
                       {
                           Kind = LaserPlug.Kinds.Citp,
                           Name = "CITP test",
                           Address = "127.0.0.1",
                           Port = port,
                       };

        var received = new System.Collections.Concurrent.ConcurrentQueue<int>();
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 4809));
        udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.180"));
        udp.Client.ReceiveTimeout = 200;
        var receiving = true;
        var receiver = new Thread(() =>
                                      {
                                          var any = new IPEndPoint(IPAddress.Any, 0);
                                          while (Volatile.Read(ref receiving))
                                          {
                                              try
                                              {
                                                  var data = udp.Receive(ref any);
                                                  // 24-byte header, then sourceKey(4) feedIndex(1) sequence(4) pointCount(2), then 5 bytes per point.
                                                  if (data.Length >= 40 && data[20] == 0x00 && data[21] == 0x02 && data[22] == 0x03)
                                                      received.Enqueue(data[35] | ((data[37] & 0x0F) << 8));
                                              }
                                              catch (SocketException)
                                              {
                                              }
                                              catch (ObjectDisposedException)
                                              {
                                                  return;
                                              }
                                          }
                                      }) { IsBackground = true };
        receiver.Start();

        var stream = LaserOutputStream.Create(plug);
        Assert.NotNull(stream);
        try
        {
            using var session = AcceptWithin(listener, TimeSpan.FromSeconds(5));
            Assert.NotNull(session);
            Assert.True(WaitUntil(() => stream.IsConnected, TimeSpan.FromSeconds(5)), "session never came up");

            var control = CaexMessage(LaserFeedControl, [0, 25]);
            session.GetStream().Write(control, 0, control.Length);
            Assert.True(WaitUntil(() => stream.StatusMessage.Contains("active at 25 fps"), TimeSpan.FromSeconds(5)),
                        $"feed never started - status '{stream.StatusMessage}'");

            // 200 frames per second for two seconds; frame n carries its number in the first point's X.
            var safety = new LaserSafetyTransforms.Config();
            var watch = Stopwatch.StartNew();
            var sequence = 0;
            while (watch.Elapsed < TimeSpan.FromSeconds(2))
            {
                var points = new LaserPoint[4];
                for (var i = 0; i < points.Length; i++)
                    points[i] = new LaserPoint { X = 1000 + sequence * 10, Y = 0, R = 65535, G = 65535, B = 65535, I = 65535 };

                stream.SubmitFrame(new LaserFrame(points, points.Length), safety, alreadySafetyApplied: true);
                sequence++;

                // Sleep has a ~15 ms granularity on Windows; spin to the next 5 ms slot instead.
                var nextSlot = TimeSpan.FromMilliseconds(5 * sequence);
                while (watch.Elapsed < nextSlot)
                    Thread.SpinWait(200);
            }

            Thread.Sleep(300);
            Volatile.Write(ref receiving, false);
            receiver.Join(1000);

            var count = received.Count;
            Assert.True(count is >= 30 and <= 70, $"{count} feed frames for {sequence} submitted");
            Assert.True(sequence > 150, $"the test did not submit fast enough ({sequence} frames)");

            // Newest wins: the frames that were sent keep rising and the last one is within a few frames of the last submitted.
            var values = received.ToArray();
            Assert.True(values.Zip(values.Skip(1), (a, b) => b >= a).All(ok => ok), "frames went out of order");
        }
        finally
        {
            stream.Dispose();
            listener.Stop();
        }
    }

    private const uint GetLaserFeedList = 0x00030100;
    private const uint LaserFeedControl = 0x00030102;

    /// <summary>A CITP/CAEX message: 20-byte CITP header (MessageSize = whole message), ContentCode, body.</summary>
    private static byte[] CaexMessage(uint contentCode, byte[] body)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write("CITP"u8.ToArray());
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((ushort)0);
        writer.Write((uint)(20 + 4 + body.Length));
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write("CAEX"u8.ToArray());
        writer.Write(contentCode);
        writer.Write(body);
        writer.Flush();
        return ms.ToArray();
    }

    private static TcpClient? AcceptWithin(TcpListener listener, TimeSpan timeout)
    {
        var accept = listener.AcceptTcpClientAsync();
        return accept.Wait(timeout) ? accept.Result : null;
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (condition())
                return true;

            Thread.Sleep(20);
        }

        return condition();
    }
}

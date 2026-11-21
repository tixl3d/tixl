using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using TiXL.DebugClient;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>
/// Runtime smoke coverage for the operators added on the multi-feature branch. Each operator is
/// instantiated in the real editor with a minimal valid graph and must produce a non-empty result
/// without warnings. OMT has its own full round-trip test in <see cref="OmtStreamTests"/>; the
/// camera and MediaPipe operators need real capture hardware and are covered by the manual plans.
/// </summary>
[Collection("Editor")]
public sealed class NewOperatorSmokeTests(EditorFixture editor)
{
    [Fact]
    public void RenderToCubeMap_ProducesMippedCubeMap()
    {
        var client = editor.Client;
        var baseline = editor.OpenPlayground();
        var logSeq = client.GetLatestLogSeq();

        var cube = client.AddOp("CubeMesh", posX: -400);
        var draw = client.AddOp("DrawMesh", posX: -150);
        var render = client.AddOp("RenderToCubeMap", posX: 100);
        client.ConnectOps(cube, "Data", draw, "Mesh");
        client.ConnectOps(draw, "Output", render, "Command");
        client.SetInput(render, "Resolution", 64);
        client.SetInput(render, "GenerateMips", true);

        try
        {
            var cubeMap = Pull(client, render, "CubeMap");
            Assert.NotNull(cubeMap);
            Assert.Equal(6, cubeMap!["arraySize"]!.Value<int>());
            Assert.True(cubeMap["mipLevels"]!.Value<int>() > 1, "GenerateMips did not produce a mip chain");
            AssertNoErrors(client, logSeq);
        }
        finally
        {
            UndoToBaseline(client, baseline);
        }
    }

    [Fact]
    public void LaserCamera_CapturesDrawnPoints_AndFreezes()
    {
        var client = editor.Client;
        var baseline = editor.OpenPlayground();

        var grid = client.AddOp("GridPoints", posX: -400);
        var draw = client.AddOp("DrawPoints2", posX: -150);
        var camera = client.AddOp("LaserCamera", posX: 100);
        client.SetInput(grid, "CountX", 2);
        client.SetInput(grid, "CountY", 2);
        client.SetInput(grid, "CountZ", 1);
        client.ConnectOps(grid, "OutBuffer", draw, "GPoints");
        client.ConnectOps(draw, "Output", camera, "Scene");

        try
        {
            var captured = WaitForPoints(client, camera);
            Assert.True(captured > 0, "LaserCamera captured no points from DrawPoints2");

            // Update off freezes the frame even when the scene changes.
            client.SetInput(camera, "Update", false);
            client.PumpFrames(3);
            client.SetInput(grid, "CountX", 4);
            client.PumpFrames(5);
            Assert.Equal(captured, Count(client, camera, "LaserPoints"));

            client.SetInput(camera, "Update", true);
            client.PumpFrames(5);
            Assert.True(Count(client, camera, "LaserPoints") > captured, "LaserCamera did not resume after Update was re-enabled");
        }
        finally
        {
            UndoToBaseline(client, baseline);
        }
    }

    [Fact]
    public void MeshToLaserPoints_ConvertsThroughCamera_AndBuffers()
    {
        var client = editor.Client;
        var baseline = editor.OpenPlayground();
        var logSeq = client.GetLatestLogSeq();

        var cube = client.AddOp("CubeMesh", posX: -400);
        var camera = client.AddOp("LaserCamera", posX: -400, posY: 200);
        var convert = client.AddOp("MeshToLaserPoints", posX: -150);
        var buffer = client.AddOp("LaserPointsToBuffer", posX: 100);
        client.ConnectOps(cube, "Data", convert, "Mesh");
        client.ConnectOps(camera, "Reference", convert, "Camera");
        client.ConnectOps(convert, "LaserPoints", buffer, "LaserPoints");
        client.SetInput(convert, "UpdateContinuously", true);

        try
        {
            var converted = 0;
            var buffered = 0;
            for (var i = 0; i < 60 && (converted == 0 || buffered == 0); i++)
            {
                client.PumpFrames(2);
                converted = Pull(client, convert, "PointCount")?.Value<int>() ?? 0;
                buffered = Pull(client, buffer, "PointCount")?.Value<int>() ?? 0;
            }

            Assert.True(converted > 0, "MeshToLaserPoints produced no points");
            Assert.Equal(converted, buffered);
            AssertNoErrors(client, logSeq);
        }
        finally
        {
            UndoToBaseline(client, baseline);
        }
    }

    [Fact]
    public void PonkInput_StartsListening_AndDecodesFrames()
    {
        const int port = 5599;
        var client = editor.Client;
        var baseline = editor.OpenPlayground();

        var ponk = client.AddOp("PONKInput");
        client.SetInput(ponk, "Port", port);

        try
        {
            var listening = false;
            for (var i = 0; i < 40 && !listening; i++)
            {
                Thread.Sleep(200);
                client.PumpFrames(2);
                listening = (Pull(client, ponk, "StatusMessage")?.Value<string>() ?? "").Contains("Listening", StringComparison.OrdinalIgnoreCase);            }

            Assert.True(listening, "PONKInput never started listening");

            var points = new (float X, float Y, byte R, byte G, byte B)[]
                             {
                                 (-0.5f, -0.5f, 255, 0, 0),
                                 (0.0f, 0.0f, 0, 255, 0),
                                 (0.5f, 0.5f, 0, 0, 255),
                             };
            using var udp = new UdpClient();
            var frame = BuildPonkFrame(points);
            for (var i = 0; i < 3; i++)
            {
                udp.Send(frame, frame.Length, new IPEndPoint(IPAddress.Loopback, port));
                client.PumpFrames(2);
            }

            var received = 0;
            for (var i = 0; i < 30 && received == 0; i++)
            {
                client.PumpFrames(2);
                received = (int)(Pull(client, ponk, "FrameCount")?.Value<float>() ?? 0);
            }

            Assert.True(received >= 1, "PONKInput received no frames");
            Assert.Equal(points.Length, Count(client, ponk, "LaserPoints"));
            Assert.Equal("SmokeTest", Pull(client, ponk, "SenderName")?.Value<string>());
        }
        finally
        {
            UndoToBaseline(client, baseline);
        }
    }

    private static JToken? Pull(DebugProtocolClient client, Guid childId, string outputName)
        => client.Call("getOutput", new JObject
                                        {
                                            ["childId"] = childId,
                                            ["outputName"] = outputName,
                                            ["update"] = true,
                                        }).Require("getOutput")["value"];

    private static int Count(DebugProtocolClient client, Guid childId, string outputName)
        => Pull(client, childId, outputName)?["elementCount"]?.Value<int>() ?? 0;

    private static int WaitForPoints(DebugProtocolClient client, Guid childId)
    {
        var count = 0;
        for (var i = 0; i < 50 && count == 0; i++)
        {
            client.PumpFrames(2);
            count = Count(client, childId, "LaserPoints");
        }

        return count;
    }

    /// <summary>Fails when the operators under test logged warnings or errors since <paramref name="logSeq"/>.</summary>
    private static void AssertNoErrors(DebugProtocolClient client, long logSeq)
    {
        var entries = client.GetLogTail(sinceSeq: logSeq, minLevel: "warning");
        Assert.True(entries.Count == 0,
                    "warnings during run: " + string.Join(" | ", entries.Select(e => e["message"])));
    }

    /// <summary>Undoes the test graph, then asserts the playground is back at its baseline size.</summary>
    private static void UndoToBaseline(DebugProtocolClient client, (int Children, int Connections) baseline)
    {
        for (var i = 0; i < 20; i++)
        {
            var state = client.GetGraphState();
            if (state["children"]!.Count() == baseline.Children && state["connections"]!.Count() == baseline.Connections)
                return;

            client.Undo();
            client.PumpFrames(3);
        }

        throw new InvalidOperationException("Could not undo the test graph back to the playground baseline");
    }

    /// <summary>One XY_F32_RGB_U8 PONK frame in a single datagram (52-byte header, byte-sum crc).</summary>
    private static byte[] BuildPonkFrame((float X, float Y, byte R, byte G, byte B)[] points)
    {
        using var payloadStream = new MemoryStream();
        using (var writer = new BinaryWriter(payloadStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)1); // XY_F32_RGB_U8
            writer.Write((byte)2); // meta entries
            WriteMeta(writer, "PATHNUMB", 1.0f);
            WriteMeta(writer, "MAXSPEED", 1.0f);
            writer.Write((ushort)points.Length);
            foreach (var point in points)
            {
                writer.Write(point.X);
                writer.Write(point.Y);
                writer.Write(point.R);
                writer.Write(point.G);
                writer.Write(point.B);
            }
        }

        var payload = payloadStream.ToArray();
        var datagram = new byte[52 + payload.Length];
        Encoding.ASCII.GetBytes("PONK-UDP").CopyTo(datagram, 0);
        datagram[8] = 0; // protocol version
        BitConverter.GetBytes(42).CopyTo(datagram, 9);
        Encoding.ASCII.GetBytes("SmokeTest").CopyTo(datagram, 13);
        datagram[45] = 1; // frame number
        datagram[46] = 1; // chunk count
        datagram[47] = 0; // chunk index
        BitConverter.GetBytes(payload.Aggregate(0u, (crc, b) => crc + b)).CopyTo(datagram, 48);
        payload.CopyTo(datagram, 52);
        return datagram;
    }

    private static void WriteMeta(BinaryWriter writer, string key, float value)
    {
        var keyBytes = new byte[8];
        Encoding.ASCII.GetBytes(key, keyBytes);
        writer.Write(keyBytes);
        writer.Write(value);
    }
}

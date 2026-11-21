using Newtonsoft.Json.Linq;
using TiXL.DebugClient;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>
/// Lib.Tests drives PhysicsSimulation directly, so it cannot catch a slot type that never resolves
/// in the editor, a world that never steps on the graph clock, or a pose buffer that never reaches
/// the GPU. This runs the three physics operators in a live editor and reads the posed points back
/// out of the uploaded buffer.
/// </summary>
[Collection("Editor")]
public sealed class PhysicsOperatorsTests(EditorFixture editor)
{
    [Fact]
    public void BodiesFallAndTheWorldPublishesTheirPoses()
    {
        var client = editor.Client;
        var baseline = editor.OpenPlayground();
        var baselineLogSeq = client.GetLatestLogSeq();

        // The world takes its time from the timeline. Reading the speed with no argument reports it
        // without changing it; the test then lets the timeline play, because the editor keeps its
        // clock moving on its own and synthetic setTime values would read as rewinds (which rebuild
        // the world) whenever they fell behind that drift.
        var wasPlaying = client.Call("setPlayback").Require("setPlayback")["playbackSpeed"]!.Value<double>() > 0.5;
        client.Call("setPlayback", new { playing = true }).Require("setPlayback");

        try
        {
            var world = client.AddOp("PhysicsWorld", posX: 340);
            var bodyA = client.AddOp("RigidBody");
            var bodyB = client.AddOp("RigidBody", posX: 170);

            Connect(client, bodyA, "OutBody", world, "Bodies", 0);
            Connect(client, bodyB, "OutBody", world, "Bodies", 1);

            client.SetInput(bodyA, "Position", Vector(0, 100, 0));
            client.SetInput(bodyB, "Position", Vector(10, 100, 0));

            // Pinning the world is what makes it evaluate every frame on the render thread. A draw
            // consumer would work too, but it needs the renderer's shader toolchain, which is a
            // machine setup this test should not depend on.
            client.Pin(world);
            client.PumpFrames(5);

            // If the PhysicsBody/PhysicsJoint slot types hadn't resolved, the ops would never have
            // loaded and this composition would be empty.
            Assert.Equal(2, OutputInt(client, world, "BodyCount"));

            var buffer = Pull(client, world, "OutBuffer", readCount: 2)["value"] as JObject;
            Assert.NotNull(buffer);
            Assert.Equal(64, buffer["stride"]!.Value<int>());
            Assert.Equal(2, buffer["elementCount"]!.Value<int>());

            var startSimTime = OutputFloat(client, world, "SimTime");
            var startY = buffer["elements"]![0]!["position"]![1]!.Value<float>();

            // Let the timeline advance; the bodies must keep falling and the world must keep stepping.
            client.PumpFrames(45);

            var simTime = OutputFloat(client, world, "SimTime");
            Assert.True(simTime > startSimTime, $"the world should have stepped, SimTime {startSimTime} -> {simTime}");

            buffer = Pull(client, world, "OutBuffer", readCount: 2)["value"] as JObject;
            Assert.NotNull(buffer);
            var elements = buffer["elements"]!.ToArray();
            Assert.Equal(2, elements.Length);

            var ax = elements[0]["position"]![0]!.Value<float>();
            var ay = elements[0]["position"]![1]!.Value<float>();
            var bx = elements[1]["position"]![0]!.Value<float>();
            var by = elements[1]["position"]![1]!.Value<float>();

            Assert.True(ay < startY - 0.2f, $"body A should have fallen, {startY} -> {ay}");
            Assert.True(by < 100f && by > 0f, $"body B is at an implausible height: {by}");
            Assert.InRange(ax, -0.05f, 0.05f); // nothing pushes them sideways
            Assert.InRange(bx, 9.95f, 10.05f);
            Assert.InRange(Math.Abs(ay - by), 0f, 0.05f); // identical initial conditions, identical fall

            var errors = client.GetLogTail(sinceSeq: baselineLogSeq, minLevel: "error");
            Assert.True(errors.Count == 0, "errors during run: " + string.Join(" | ", errors.Select(e => e["message"])));
        }
        finally
        {
            var state = client.GetGraphState();
            for (var i = 0; i < 16 && state["children"]!.Count() > baseline.Children; i++)
            {
                client.Undo();
                state = client.GetGraphState();
            }

            client.Call("setPlayback", new { playing = wasPlaying });
        }
    }

    /// <summary>Connects into a multi-input; the world collects one body per input index.</summary>
    private static void Connect(DebugProtocolClient client, Guid source, string output, Guid target, string input, int index)
        => client.Call("connect", new
                                     {
                                         sourceChildId = source,
                                         sourceOutput = output,
                                         targetChildId = target,
                                         targetInput = input,
                                         multiInputIndex = index,
                                     }).Require("connect");

    /// <summary>Reads a cached output value; with readCount a buffer is read back from the GPU.</summary>
    private static JObject Pull(DebugProtocolClient client, Guid childId, string outputName, int readCount = 0)
    {
        var request = new JObject { ["childId"] = childId, ["outputName"] = outputName };
        if (readCount > 0)
            request["readCount"] = readCount;

        return client.Call("getOutput", request).Require("getOutput");
    }

    private static int OutputInt(DebugProtocolClient client, Guid childId, string outputName)
        => Pull(client, childId, outputName)["value"]!.Value<int>();

    private static float OutputFloat(DebugProtocolClient client, Guid childId, string outputName)
        => Pull(client, childId, outputName)["value"]!.Value<float>();

    private static JObject Vector(float x, float y, float z) => new() { ["X"] = x, ["Y"] = y, ["Z"] = z };
}

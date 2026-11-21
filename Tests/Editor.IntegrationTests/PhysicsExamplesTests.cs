using Newtonsoft.Json.Linq;
using TiXL.DebugClient;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>
/// Every physics example has to be a working graph, not just a plausible-looking .t3: a stale symbol
/// id or a wrong output id leaves the example evaluating to nothing without logging anything, which
/// is exactly how these were once broken. Each one is instantiated in the playground, pinned and
/// pumped, and must produce its RenderTarget texture.
/// </summary>
[Collection("Editor")]
public sealed class PhysicsExamplesTests(EditorFixture editor)
{
    [Theory]
    [InlineData("PhysicsWorldExample")]
    [InlineData("RigidBodyExample")]
    [InlineData("RigidJointExample")]
    [InlineData("RigidForceExample")]
    [InlineData("RigidBodyGeometryExample")]
    [InlineData("RigidBodyPointsExample")]
    [InlineData("MeshToGeometryExample")]
    [InlineData("PhysicsVolumesExample")]
    [InlineData("PhysicsSdfFieldExample")]
    [InlineData("PhysicsVolumeGravityExample")]
    [InlineData("PhysicsToVolumeExample")]
    public void ExampleProducesItsOutputTexture(string name)
    {
        var client = editor.Client;
        var baseline = editor.OpenPlayground();

        try
        {
            var example = client.AddOp(name);
            client.Pin(example);
            client.PumpFrames(20);

            var result = client.Call("getOutput", new { childId = example, outputName = "Output" }).Require("getOutput");
            Assert.Equal("Texture2D", result["valueType"]!.Value<string>());

            // A null texture means the graph inside the example never evaluated - the failure mode a
            // mismatched symbol or output id produces silently.
            var value = result["value"];
            Assert.True(value is not null && value.Type != JTokenType.Null, $"{name}: Output texture is null");
        }
        finally
        {
            var state = client.GetGraphState();
            for (var i = 0; i < 8 && state["children"]!.Count() > baseline.Children; i++)
            {
                client.Undo();
                state = client.GetGraphState();
            }
        }
    }
}

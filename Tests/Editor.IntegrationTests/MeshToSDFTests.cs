using Newtonsoft.Json.Linq;
using TiXL.DebugClient;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>
/// Accepts the MeshToSDF operator end to end: the op is registered, every bake compute
/// kernel compiles, a cube actually bakes, and a rebake at a larger resolution and in a
/// different mode still succeeds without logging errors. Runs in the _agentTests
/// playground and leaves it at its baseline.
/// </summary>
[Collection("Editor")]
public sealed class MeshToSDFTests(EditorFixture editor)
{
    [Fact]
    public void BakesCubeVolume_AndRebakesInEveryMode()
    {
        var client = editor.Client;
        var baseline = editor.OpenPlayground();
        client.Call("getShaderErrors", new { clear = true });
        var baselineLogSeq = client.GetLatestLogSeq();

        var cube = client.AddOp("CubeMesh");
        var meshToSdf = client.AddOp("MeshToSDF", posX: 300);

        try
        {
            client.ConnectOps(cube, "Data", meshToSdf, "Mesh");
            client.Pin(meshToSdf);
            client.PumpFrames(30);

            Assert.True(WaitForBakeCount(client, meshToSdf, 1f), "the default bake should complete");

            // Auto-fit must scale the fitted box to the full voxel budget: with a mesh
            // larger than the manual Size the volume still has to be exactly Resolution
            // voxels on its longest axis (a pre-fit scale would allocate more than that).
            var debugLog = client.GetLogTail(sinceSeq: baselineLogSeq, minLevel: "debug", maxCount: 500);
            var baked = debugLog.Select(e => e["message"]?.Value<string>() ?? string.Empty)
                                .FirstOrDefault(m => m.Contains("baked SDF volume"));
            Assert.NotNull(baked);
            Assert.Contains("128 voxels max axis", baked);

            // A changed resolution re-allocates the working volumes (the memory-heavy path).
            client.SetInput(meshToSdf, "Resolution", new JValue(256f));
            Assert.True(WaitForBakeCount(client, meshToSdf, 2f), "the bake at Resolution 256 should complete");

            // Every conversion mode exercises a different kernel path.
            foreach (var mode in new[] { 1, 2, 3, 0 })
            {
                client.SetInput(meshToSdf, "Mode", new JValue(mode));
                var target = BakeCount(client, meshToSdf) + 1f;
                Assert.True(WaitForBakeCount(client, meshToSdf, target, iterations: 40), $"mode {mode} should bake");
            }

            var shaderErrors = (JArray)client.Call("getShaderErrors", new { }).Require("getShaderErrors")["failures"]!;
            Assert.True(shaderErrors.Count == 0,
                        "shader compile failures: " + string.Join(" | ", shaderErrors.Select(e => e["name"] + "/" + e["entryPoint"])));

            var errors = client.GetLogTail(sinceSeq: baselineLogSeq, minLevel: "warning");
            Assert.True(errors.Count == 0, "errors during run: " + string.Join(" | ", errors.Select(e => e["message"])));
        }
        finally
        {
            // Undo every command this test issued (2 adds, a connect, 5 set-inputs) and
            // verify the playground is really back at its baseline.
            for (var i = 0; i < 24; i++)
            {
                var state = client.GetGraphState();
                if (state["children"]!.Count() <= baseline.Children
                    && state["connections"]!.Count() <= baseline.Connections)
                    break;

                client.Undo();
                client.PumpFrames(2);
            }

            var finalState = client.GetGraphState();
            Assert.True(finalState["children"]!.Count() == baseline.Children
                        && finalState["connections"]!.Count() == baseline.Connections,
                        "the playground should be restored to its baseline");
        }
    }

    private static float BakeCount(DebugProtocolClient client, Guid childId)
    {
        var response = client.Call("getOutput", new { childId, outputName = "BakeCount", update = true });
        return response.Ok ? response.Result?["value"]?.Value<float>() ?? -1f : -1f;
    }

    private static bool WaitForBakeCount(DebugProtocolClient client, Guid childId, float target, int iterations = 20)
    {
        for (var i = 0; i < iterations; i++)
        {
            if (BakeCount(client, childId) >= target)
                return true;

            client.PumpFrames(10);
        }

        return BakeCount(client, childId) >= target;
    }
}

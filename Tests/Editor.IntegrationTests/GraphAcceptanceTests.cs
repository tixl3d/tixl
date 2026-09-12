using Newtonsoft.Json.Linq;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>
/// The debug-protocol plan's Phase 3 acceptance criterion: create ops, connect them,
/// change a parameter, screenshot, undo - app never touched by hand. Runs in the
/// _agentTests playground and leaves it at its baseline.
/// </summary>
[Collection("Editor")]
public sealed class GraphAcceptanceTests(EditorFixture editor)
{
    // Direct .t3 forwarding must retain dirty propagation and fallback values after undo, redo, and reload.
    [Fact]
    public void RerouteFloat_ForwardsChangesAcrossReloadAndConnectionUndo()
    {
        var client = editor.Client;
        editor.OpenPlayground();
        var initial = client.GetGraphState();
        var row = initial["children"]!.Select(c => c["posY"]?.Value<float>() ?? 0).DefaultIfEmpty(0).Max() + 250;
        var source = client.AddOp("RerouteFloat", posY: row);
        var target = client.AddOp("RerouteFloat", posX: 220, posY: row);
        client.SetInput(source, "Input", JToken.FromObject(12.5f));
        client.SetInput(target, "Input", JToken.FromObject(-3f));
        client.ConnectOps(source, "Output", target, "Input");
        client.Select(target);
        client.PumpFrames(10);

        Assert.Equal(12.5f, ReadOutput(target));
        client.Undo();
        client.PumpFrames(5);
        Assert.Equal(-3f, ReadOutput(target));
        client.Call("redo").Require("redo reroute connection");
        client.PumpFrames(5);
        Assert.Equal(12.5f, ReadOutput(target));

        client.SetInput(source, "Input", JToken.FromObject(27.25f));
        client.PumpFrames(10);
        Assert.Equal(27.25f, ReadOutput(target));
        client.Reload(EditorFixture.PlaygroundProject).Require("reload routed graph");
        client.Select(target);
        client.PumpFrames(10);
        Assert.Equal(27.25f, ReadOutput(target));

        var state = client.GetGraphState();
        Assert.Contains(state["connections"]!, c => c["sourceParentOrChildId"]!.Value<string>() == source.ToString()
                                                     && c["targetParentOrChildId"]!.Value<string>() == target.ToString());

        // The parameter panel and debug bridge share this command, bypassing the graph hotkey filter.
        var commandAnchor = client.AddOp("RerouteCommand", posX: 440, posY: row);
        var bypass = client.Call("setBypass", new { childId = commandAnchor, bypassed = true }).Require("try bypassing a command anchor");
        Assert.False(bypass["bypassed"]!.Value<bool>());
        client.Select(target);
        client.PumpFrames(5);
        Assert.Equal(27.25f, ReadOutput(target));

        float ReadOutput(Guid childId)
            => client.Call("getOutput", new { childId, update = true }).Require("read forwarded float")["value"]!.Value<float>();
    }

    [Fact]
    public void BuildRenderRecolorUndo_RoundTrips()
    {
        var client = editor.Client;
        var baseline = editor.OpenPlayground();
        var baselineLogSeq = client.GetLatestLogSeq();

        // Build CubeMesh -> DrawMesh
        var cube = client.AddOp("CubeMesh");
        var draw = client.AddOp("DrawMesh", posX: 250);
        client.ConnectOps(cube, "Data", draw, "Mesh");

        var state = client.GetGraphState();
        Assert.Equal(baseline.Children + 2, state["children"]!.Count());
        Assert.Contains(state["connections"]!,
                        c => c["sourceParentOrChildId"]!.Value<string>() == cube.ToString()
                             && c["targetParentOrChildId"]!.Value<string>() == draw.ToString());

        // Render and capture
        client.Pin(draw);
        client.PumpFrames(30);
        var shotDir = Path.Combine(Path.GetTempPath(), "tixl-debug");
        Directory.CreateDirectory(shotDir);
        var shot1 = Path.Combine(shotDir, "test-cube-default.png");
        var shot2 = Path.Combine(shotDir, "test-cube-red.png");
        client.Screenshot(shot1);
        client.PumpFrames(10);
        Assert.True(new FileInfo(shot1).Length > 2000, "first screenshot should have content");

        // Recolor must alter the image
        client.SetVector4Input(draw, "Color", 1f, 0.2f, 0.1f, 1f);
        client.PumpFrames(15);
        client.Screenshot(shot2);
        client.PumpFrames(10);
        Assert.False(File.ReadAllBytes(shot1).AsSpan().SequenceEqual(File.ReadAllBytes(shot2)),
                     "parameter change should alter the rendered image");

        // Undo everything back to the baseline
        for (var i = 0; i < 10; i++)
        {
            state = client.GetGraphState();
            if (state["children"]!.Count() == baseline.Children
                && state["connections"]!.Count() == baseline.Connections)
                break;

            client.Undo();
            client.PumpFrames(3);
        }

        state = client.GetGraphState();
        Assert.Equal(baseline.Children, state["children"]!.Count());
        Assert.Equal(baseline.Connections, state["connections"]!.Count());

        // No errors logged along the way
        var errors = client.GetLogTail(sinceSeq: baselineLogSeq, minLevel: "error");
        Assert.True(errors.Count == 0, "errors during run: " + string.Join(" | ", errors.Select(e => e["message"])));
    }

    [Fact]
    public void AddOp_UnknownSymbol_ReturnsNotFound()
    {
        editor.OpenPlayground();
        var response = editor.Client.Call("addOp", new { symbolName = "DefinitelyNoSuchOperator" });
        Assert.False(response.Ok);
        Assert.Equal("NOT_FOUND", response.ErrorCode);
    }

    [Fact]
    public void SetInput_ByName_IsCaseInsensitive()
    {
        var client = editor.Client;
        var baseline = editor.OpenPlayground();
        var cube = client.AddOp("CubeMesh");
        try
        {
            client.SetInput(cube, "scale", JToken.FromObject(2.0f));
        }
        finally
        {
            // Undo the set-input (if it succeeded) and the add.
            var state = client.GetGraphState();
            for (var i = 0; i < 4 && state["children"]!.Count() > baseline.Children; i++)
            {
                client.Undo();
                state = client.GetGraphState();
            }
        }
    }
}

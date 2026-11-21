using Newtonsoft.Json.Linq;
using TiXL.DebugClient;
using Xunit;

namespace Editor.IntegrationTests;

/// <summary>
/// The OMT round trip through the actual graphics pipeline: a generated texture is sent by an OmtOutput op and
/// read back by an OmtInput op in the same process. That is the only place the UYVY packing shader, the staging
/// readback, the VMX codec and the receive-side upload are all exercised together.
/// </summary>
[Collection("Editor")]
public sealed class OmtStreamTests(EditorFixture editor)
{
    [Theory]
    [InlineData(false)] // opaque: frames are packed to UYVY on the GPU
    [InlineData(true)]  // alpha: frames go as BGRA and an RGBA source is channel-swapped for OMT
    public void OmtOutputToInput_RoundTripsTexture(bool enableAlpha)
    {
        var client = editor.Client;
        var baseline = editor.OpenPlayground();
        var baselineLogSeq = client.GetLatestLogSeq();

        // A white 320x180 texture in the 8-bit RGBA format OMT accepts.
        var source = client.AddOp("RenderTarget", posX: -400);
        client.SetInput(source, "Resolution", new JObject { ["X"] = 320, ["Y"] = 180 });
        client.SetInput(source, "TextureFormat", "R8G8B8A8_UNorm");
        client.SetVector4Input(source, "ClearColor", 1f, 1f, 1f, 1f);
        client.PumpFrames(5);

        var sourceDescription = client.Call("getOutput", new { childId = source, update = true }).Require("getOutput")["value"];
        Assert.True(sourceDescription is { Type: JTokenType.Object }, $"the source produced no texture: {sourceDescription}");
        var expectedWidth = sourceDescription!["width"]!.Value<int>();
        var expectedHeight = sourceDescription["height"]!.Value<int>();
        Assert.True(expectedWidth > 0 && expectedHeight > 0, $"the source produced no texture: {sourceDescription}");

        // Confirm the harness can see white before trusting it on the receive side.
        Assert.True(MeasureBrightness(client, source, "ColorBuffer") > 0.2f, "the white source did not read back as bright");

        // Send it.
        var senderName = "OmtTest_" + Guid.NewGuid().ToString("N")[..6];
        var omtOutput = client.AddOp("OmtOutput", posX: 0);
        client.SetInput(omtOutput, "SenderName", senderName);
        client.SetInput(omtOutput, "EnableAlpha", enableAlpha);
        client.ConnectOps(source, "ColorBuffer", omtOutput, "Texture");

        // Pulling the op is what evaluates it outside a pinned view; the address is only known once it sent.
        string? address = null;
        for (var i = 0; i < 20 && address == null; i++)
        {
            var result = client.Call("getOutput", new { childId = omtOutput, outputName = "SourceAddress", update = true })
                               .Require("getOutput");
            address = result["value"]?.Type == JTokenType.Null ? null : result["value"]?.Value<string>();
            client.PumpFrames(2);
        }

        Assert.False(string.IsNullOrEmpty(address), "OmtOutput never reported a discoverable source address");
        Assert.Contains(senderName, address);

        // Receive it by the name the sender advertises, which is exactly what a user picks in the dropdown.
        var omtInput = client.AddOp("OmtInput", posX: 400);
        client.SetInput(omtInput, "SourceName", address!);

        JToken? received = null;
        for (var i = 0; i < 60; i++)
        {
            client.PumpFrames(2);

            // Without a pull the sender's update only runs when the graph is visited.
            client.Call("getOutput", new { childId = omtOutput, outputName = "SourceAddress", update = true });

            var description = client.Call("getOutput", new { childId = omtInput, update = true }).Require("getOutput")["value"];
            if (description == null || description.Type == JTokenType.Null)
                continue;

            received = description;
            break;
        }

        Assert.NotNull(received);
        Assert.Equal(expectedWidth, received!["width"]!.Value<int>());
        Assert.Equal(expectedHeight, received["height"]!.Value<int>());
        Assert.Equal("B8G8R8A8_UNorm", received["format"]!.Value<string>());

        // Pixels, not just dimensions: the frame was white, so it has to arrive bright.
        var receivedBrightness = MeasureBrightness(client, omtInput, "Texture", omtOutput);
        Assert.True(receivedBrightness > 0.2f, $"the received frame is too dark: {receivedBrightness}");

        // Undo back to the baseline
        for (var i = 0; i < 10; i++)
        {
            var state = client.GetGraphState();
            if (state["children"]!.Count() == baseline.Children && state["connections"]!.Count() == baseline.Connections)
                break;

            client.Undo();
            client.PumpFrames(3);
        }

        var errors = client.GetLogTail(sinceSeq: baselineLogSeq, minLevel: "error");
        Assert.True(errors.Count == 0, "errors during run: " + string.Join(" | ", errors.Select(e => e["message"])));

        // The transport logs its own warnings ("OMT runtime not found", a dropped channel); surface them.
        var transportWarnings = client.GetLogTail(sinceSeq: baselineLogSeq, minLevel: "warning")
                                      .Where(e => (e["message"]?.Value<string>() ?? string.Empty).Contains("OMT", StringComparison.OrdinalIgnoreCase)
                                                  || (e["message"]?.Value<string>() ?? string.Empty).Contains("libvmx", StringComparison.OrdinalIgnoreCase))
                                      .Select(e => e["message"]!.Value<string>())
                                      .ToList();
        Assert.True(transportWarnings.Count == 0, "OMT warned: " + string.Join(" | ", transportWarnings));
    }

    /// <summary>
    /// Average brightness of an image op's texture. The analysis composition dispatches its compute shader from
    /// its "Update" command output, so that has to be pulled before the reduced value means anything. A sender
    /// passed in keeps producing frames while the probe waits for one to arrive.
    /// </summary>
    private static float MeasureBrightness(DebugProtocolClient client, Guid imageChildId, string imageOutput, Guid? senderToKeepPulling = null)
    {
        var probe = client.AddOp("GetImageBrightness", posX: 800);
        client.ConnectOps(imageChildId, imageOutput, probe, "Texture2d");

        try
        {
            for (var i = 0; i < 30; i++)
            {
                client.PumpFrames(2);

                if (senderToKeepPulling != null)
                    client.Call("getOutput", new { childId = senderToKeepPulling, outputName = "SourceAddress", update = true });

                client.Call("getOutput", new { childId = probe, outputName = "Update", update = true });
                var value = client.Call("getOutput", new { childId = probe, outputName = "Brightness", update = true })
                                  .Require("getOutput")["value"];

                if (value is { Type: JTokenType.Float } or { Type: JTokenType.Integer })
                    return value.Value<float>();
            }

            return float.NaN;
        }
        finally
        {
            client.DeleteOp(probe);
        }
    }
}

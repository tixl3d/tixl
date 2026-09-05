using Newtonsoft.Json.Linq;
using Xunit;

namespace Editor.IntegrationTests;

[Collection("Editor")]
public sealed class ProtocolBasicsTests(EditorFixture editor)
{
    [Fact]
    public void Ping_ReturnsEnvelopeWithFrameAndStructureVersion()
    {
        var response = editor.Client.Ping();
        Assert.True(response.Ok);
        Assert.True(response.Frame >= 0);
        Assert.True(response.StructureVersion >= 0);
    }

    [Fact]
    public void GetVersion_ReportsProtocolVersion()
    {
        var result = editor.Client.Call("getVersion").Require("getVersion");
        Assert.Equal(1, result["protocolVersion"]!.Value<int>());
    }

    [Fact]
    public void GetMetrics_ReportsGpuMemoryAndFrameTiming()
    {
        var metrics = editor.Client.GetMetrics();
        Assert.True(metrics["frameDeltaSeconds"]!.Value<float>() > 0);
        Assert.NotNull(metrics["gpuMemory"]);
    }

    [Fact]
    public void GetLogTail_ReturnsSequencedEntries()
    {
        var entries = editor.Client.GetLogTail(maxCount: 5);
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.True(e["seq"]!.Value<long>() >= 0));
    }

    [Fact]
    public void UnknownMethod_ReturnsCleanError()
    {
        var response = editor.Client.Call("definitelyNotAMethod");
        Assert.False(response.Ok);
        Assert.Equal("UNKNOWN_METHOD", response.ErrorCode);
    }
}

using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Settings;
using T3.Core.Video;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Verifies the per-project proxy settings (added in proxy-media Phase 3) survive a .t3 write/read round-trip
/// and that files written before the Proxy section existed fall back to the defaults.
/// </summary>
public class CompositionSettingsProxyTests
{
    [Fact]
    public void ProxySettings_RoundTrip()
    {
        var original = new CompositionSettings { Enabled = true };
        original.Proxy.Format = VideoExportCodec.Hap;
        original.Proxy.Resolution = 0.25f;
        original.Proxy.UseForPreview = false;

        var restored = WriteThenRead(original);

        Assert.NotNull(restored);
        Assert.Equal(VideoExportCodec.Hap, restored!.Proxy.Format);
        Assert.Equal(0.25f, restored.Proxy.Resolution);
        Assert.False(restored.Proxy.UseForPreview);
    }

    [Fact]
    public void NonDefaultProxy_PersistsEvenWhenNotEnabled()
    {
        // The Proxy section must round-trip on its own merit, not only when Enabled / audio clips force a write.
        var original = new CompositionSettings { Enabled = false };
        original.Proxy.Format = VideoExportCodec.HapQ;

        var restored = WriteThenRead(original);

        Assert.NotNull(restored);
        Assert.Equal(VideoExportCodec.HapQ, restored!.Proxy.Format);
    }

    [Fact]
    public void MissingProxySection_FallsBackToDefaults()
    {
        // A file written before the Proxy section existed: ProjectSettings with a Playback sub-object but no Proxy.
        var symbolToken = new JObject
                              {
                                  ["ProjectSettings"] = new JObject
                                                            {
                                                                ["Enabled"] = true,
                                                                ["Playback"] = new JObject { ["Bpm"] = 120f },
                                                            },
                              };

        var restored = CompositionSettings.ReadFromJson(symbolToken);

        Assert.NotNull(restored);
        Assert.Equal(VideoExportCodec.ProRes, restored!.Proxy.Format);
        Assert.Equal(0.5f, restored.Proxy.Resolution);
        Assert.True(restored.Proxy.UseForPreview);
    }

    private static CompositionSettings? WriteThenRead(CompositionSettings settings)
    {
        var sb = new StringBuilder();
        using (var sw = new StringWriter(sb))
        using (var writer = new JsonTextWriter(sw))
        {
            writer.WriteStartObject();
            settings.WriteToJson(writer);
            writer.WriteEndObject();
            writer.Flush();
        }

        return CompositionSettings.ReadFromJson(JObject.Parse(sb.ToString()));
    }
}

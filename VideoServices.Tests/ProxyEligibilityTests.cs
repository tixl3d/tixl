using System;
using System.IO;
using System.Threading;
using T3.Core.Video;
using Xunit;

namespace T3.VideoServices.Tests;

/// <summary>
/// Confirms the proxy-eligibility probe is codec-agnostic: a real long-GOP H.264 is "recommended", and the
/// all-intra ProRes proxy transcoded from it is "not needed" — proving the decision rides on measured keyframe
/// spacing, not a codec allow-list.
/// </summary>
public class ProxyEligibilityTests
{
    [Fact]
    public void LongGopSource_IsRecommended()
    {
        // test-720p.mp4 is H.264 with a ~1 s GOP (keyframe every 30 frames) — a normal inter-frame cadence.
        var result = ProxyEligibility.Evaluate(TestAssets.Video720p);

        Assert.Equal(ProxyEligibility.Recommendation.Recommended, result.Recommendation);
        Assert.True(result.KeyframeIntervalSeconds > ProxyEligibility.LongGopSeconds,
                    $"expected a long GOP, measured {result.KeyframeIntervalSeconds:0.00}s ({result.Reason})");
    }

    [Fact]
    public void AllIntraProxy_IsNotNeeded()
    {
        var proxy = Path.Combine(Path.GetTempPath(), $"tixl-eligibility-{Guid.NewGuid():N}.mov");
        try
        {
            var error = ProxyTranscoder.Generate(TestAssets.Video720p, proxy, VideoExportCodec.ProRes, 0.5, null, CancellationToken.None);
            Assert.Null(error);

            var result = ProxyEligibility.Evaluate(proxy);

            Assert.Equal(ProxyEligibility.Recommendation.NotNeeded, result.Recommendation);
            Assert.True(result.KeyframeIntervalSeconds < ProxyEligibility.LongGopSeconds,
                        $"all-intra should seek cheaply, measured {result.KeyframeIntervalSeconds:0.00}s ({result.Reason})");
        }
        finally
        {
            if (File.Exists(proxy))
                File.Delete(proxy);
        }
    }

    [Fact]
    public void MissingSource_IsUnknown()
    {
        var result = ProxyEligibility.Evaluate("does-not-exist.mp4");
        Assert.Equal(ProxyEligibility.Recommendation.Unknown, result.Recommendation);
    }
}

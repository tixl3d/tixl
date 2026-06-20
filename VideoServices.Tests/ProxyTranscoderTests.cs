using System;
using System.IO;
using System.Threading;
using Sdcb.FFmpeg.Codecs;
using T3.Core.Video;
using Xunit;

namespace T3.VideoServices.Tests;

/// <summary>
/// Exercises the proxy transcode end-to-end: take a real long-GOP H.264 source, transcode it to a downscaled
/// all-intra proxy, then decode the proxy back and confirm it's a playable, smaller-resolution video.
/// </summary>
public class ProxyTranscoderTests
{
    [Theory]
    [InlineData(VideoExportCodec.ProRes)]
    [InlineData(VideoExportCodec.Hap)] // skipped on builds without the hap encoder
    public void Generate_ProducesPlayableDownscaledProxy(VideoExportCodec codec)
    {
        if (codec is VideoExportCodec.Hap && Codec.FindEncoderByName("hap") == null)
            return;

        var source = TestAssets.Video720p;
        int sourceWidth;
        using (var src = VideoDecoderSession.TryOpen(source, VideoPlaybackOptimization.FastSeeking, out _))
        {
            Assert.NotNull(src);
            sourceWidth = src!.Width;
        }

        var proxy = Path.Combine(Path.GetTempPath(), $"tixl-proxy-{codec}-{Guid.NewGuid():N}{codec.GetFileExtension()}");
        try
        {
            var error = ProxyTranscoder.Generate(source, proxy, codec, 0.5, null, CancellationToken.None);
            Assert.Null(error);
            Assert.True(File.Exists(proxy));
            Assert.True(new FileInfo(proxy).Length > 0, "proxy file is empty");

            // The encode goes to a temp ".partial" sibling that is atomically swapped in; it must not linger
            // (a stray partial next to the source is what the engine could otherwise pick up half-written).
            var partial = Path.Combine(Path.GetDirectoryName(proxy)!,
                                       Path.GetFileNameWithoutExtension(proxy) + ".partial" + Path.GetExtension(proxy));
            Assert.False(File.Exists(partial), "temp .partial proxy should have been swapped away");

            using var session = VideoDecoderSession.TryOpen(proxy, VideoPlaybackOptimization.FastSeeking, out var openError);
            Assert.Null(openError);
            Assert.NotNull(session);

            // Downscaled to ~half the source width.
            Assert.True(session!.Width < sourceWidth, "proxy should be smaller than the source");
            Assert.InRange(session.Width, sourceWidth / 2 - 4, sourceWidth / 2 + 4);

            var decoded = 0;
            while (session.TryReadNextFrame(out _))
                decoded++;
            Assert.True(decoded > 1, $"proxy decoded only {decoded} frames");
        }
        finally
        {
            if (File.Exists(proxy))
                File.Delete(proxy);
        }
    }

    [Fact]
    public void Generate_MissingSource_ReturnsError()
    {
        var proxy = Path.Combine(Path.GetTempPath(), $"tixl-proxy-missing-{Guid.NewGuid():N}.mov");
        var error = ProxyTranscoder.Generate("does-not-exist.mp4", proxy, VideoExportCodec.ProRes, 0.5, null, CancellationToken.None);
        Assert.NotNull(error);
        Assert.False(File.Exists(proxy));
    }
}

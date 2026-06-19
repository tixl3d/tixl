using T3.Core.Video;
using Xunit;

namespace T3.VideoServices.Tests;

public class SoftwareFrameConverterTests
{
    [Fact]
    public void Convert_ProducesPackedRgbaOfExpectedSize()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);
        using var converter = new SoftwareFrameConverter(session!.IsHdr);

        Assert.True(session.TryReadNextFrame(out _));
        var rgba = converter.Convert(session.CurrentFrame).ToImageBuffer(1);

        Assert.Equal(session.Width * session.Height * converter.BytesPerPixel, rgba.Length);
    }

    [Fact]
    public void Convert_IsDeterministic_SameTimeYieldsSameRgba()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);
        using var converter = new SoftwareFrameConverter(session!.IsHdr);

        var early = TimeToFrameMapper.SecondsToPts(1.0, session.StreamStartPts, session.TimeBaseNum, session.TimeBaseDen);
        var later = TimeToFrameMapper.SecondsToPts(3.0, session.StreamStartPts, session.TimeBaseNum, session.TimeBaseDen);

        Assert.True(session.SeekAndDecodeTo(early, out _));
        var first = converter.Convert(session.CurrentFrame).ToImageBuffer(1);

        // Seek away and back — the full decode→RGBA pipeline must reproduce identical pixels.
        Assert.True(session.SeekAndDecodeTo(later, out _));
        Assert.True(session.SeekAndDecodeTo(early, out _));
        var second = converter.Convert(session.CurrentFrame).ToImageBuffer(1);

        Assert.Equal(first, second);
    }
}

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
        var frame = converter.Convert(session.CurrentFrame);

        Assert.Equal(session.Width, frame.Width);
        Assert.Equal(session.Height, frame.Height);
        Assert.Equal((int)converter.OutputFormat, frame.Format);
        Assert.True(frame.Linesize[0] >= session.Width * converter.BytesPerPixel);
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
        var first = CopyPixels(converter, session);

        // Seek away and back — the full decode→RGBA pipeline must reproduce identical pixels.
        Assert.True(session.SeekAndDecodeTo(later, out _));
        Assert.True(session.SeekAndDecodeTo(early, out _));
        var second = CopyPixels(converter, session);

        Assert.Equal(first, second);
    }

    // Through the converter's own copy, as the decoder does: Sdcb's ToImageBuffer fails outside Windows.
    private static byte[] CopyPixels(SoftwareFrameConverter converter, VideoDecoderSession session)
    {
        var pixels = new byte[session.Width * session.Height * converter.BytesPerPixel];
        converter.CopyPixels(converter.Convert(session.CurrentFrame), pixels);
        return pixels;
    }
}

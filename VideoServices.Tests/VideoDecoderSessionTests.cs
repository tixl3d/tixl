using Sdcb.FFmpeg.Utils;
using T3.Core.Video;
using Xunit;

namespace T3.VideoServices.Tests;

/// <summary>
/// Exercises the real FFmpeg decode + seek path against the checked-in sample videos. These assert the
/// core M1 guarantees: correct metadata, monotonic sequential decode, frame-accurate seeking, and — most
/// importantly — that the same requested time deterministically resolves to the same frame.
/// </summary>
public class VideoDecoderSessionTests
{
    [Fact]
    public void Open720p_ReadsExpectedMetadata()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out var error);

        Assert.Null(error);
        Assert.NotNull(session);
        Assert.Equal(1280, session!.Width);
        Assert.Equal(720, session.Height);
        Assert.False(session.IsHdr);
        Assert.InRange(session.FrameRate, 59.9, 60.1);
        Assert.InRange(session.DurationSeconds, 59.0, 61.0);
        Assert.True(session.TimeBaseDen > 0);
    }

    [Fact]
    public void Open1080p_Succeeds()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video1080p, VideoPlaybackOptimization.FastSeeking, out var error);

        Assert.Null(error);
        Assert.NotNull(session);
        Assert.True(session!.Width > 0 && session.Height > 0);
    }

    [Fact]
    public void Open_MissingFile_ReturnsError()
    {
        using var session = VideoDecoderSession.TryOpen("does-not-exist.mp4", VideoPlaybackOptimization.FastSeeking, out var error);

        Assert.Null(session);
        Assert.NotNull(error);
    }

    [Fact]
    public void SequentialDecode_PtsAreMonotonic()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);

        var previous = long.MinValue;
        var count = 0;
        while (count < 30 && session!.TryReadNextFrame(out var pts))
        {
            Assert.True(pts > previous, $"PTS {pts} should be > previous {previous}");
            previous = pts;
            count++;
        }

        Assert.True(count >= 30, $"Expected at least 30 frames, decoded {count}");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    public void Seek_IsFrameAccurate(double seconds)
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);

        var target = TimeToFrameMapper.SecondsToPts(seconds, session!.StreamStartPts, session.TimeBaseNum, session.TimeBaseDen);
        Assert.True(session.SeekAndDecodeTo(target, out var landed));
        Assert.Equal(target, landed);
    }

    [Fact]
    public void Seek_IsDeterministic_SameTimeYieldsSameFrame()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);

        var early = TimeToFrameMapper.SecondsToPts(1.0, session!.StreamStartPts, session.TimeBaseNum, session.TimeBaseDen);
        var later = TimeToFrameMapper.SecondsToPts(3.0, session.StreamStartPts, session.TimeBaseNum, session.TimeBaseDen);

        Assert.True(session.SeekAndDecodeTo(early, out var firstPts));
        var firstChecksum = LumaChecksum(session.CurrentFrame);

        // Seek elsewhere, then back — content must be identical, proving determinism across seeks.
        Assert.True(session.SeekAndDecodeTo(later, out _));
        Assert.True(session.SeekAndDecodeTo(early, out var secondPts));
        var secondChecksum = LumaChecksum(session.CurrentFrame);

        Assert.Equal(firstPts, secondPts);
        Assert.Equal(firstChecksum, secondChecksum);
    }

    // Cheap deterministic hash of the luma plane (sparse sample) to prove frame-content identity.
    private static unsafe long LumaChecksum(Frame frame)
    {
        var y = (byte*)frame.Data[0];
        var stride = frame.Linesize[0];
        long sum = 0;
        for (var row = 0; row < frame.Height; row++)
        {
            var line = y + (long)row * stride;
            for (var x = 0; x < frame.Width; x += 7)
                sum += line[x];
        }

        return sum;
    }
}

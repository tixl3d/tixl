using Sdcb.FFmpeg.Utils;
using T3.Core.Video;
using Xunit;

namespace T3.VideoServices.Tests;

/// <summary>
/// Covers the per-stream frame cache: a cached frame is byte-identical to a fresh decode, lookups miss
/// cleanly, the byte budget caps retention, and least-recently-used frames evict first.
/// </summary>
public class VideoFrameCacheTests
{
    [Fact]
    public void Get_AfterAdd_ReturnsIdenticalFrame()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);
        var frameBytes = FrameBytes(session!);

        var target = TimeToFrameMapper.SecondsToPts(2.0, session.StreamStartPts, session.TimeBaseNum, session.TimeBaseDen);
        Assert.True(session.SeekAndDecodeTo(target, out _));
        var expected = LumaChecksum(session.CurrentFrame);

        using var cache = new VideoFrameCache(64L * 1024 * 1024);
        cache.Add(target, session.CurrentFrame, frameBytes);

        // Move the decoder away so CurrentFrame no longer holds the target — the hit must come from the cache,
        // proving the retained clone keeps its own buffer ref after the decoder reuses its frame.
        var elsewhere = TimeToFrameMapper.SecondsToPts(5.0, session.StreamStartPts, session.TimeBaseNum, session.TimeBaseDen);
        Assert.True(session.SeekAndDecodeTo(elsewhere, out _));

        Assert.True(cache.TryGet(target, out var cached));
        Assert.Equal(expected, LumaChecksum(cached));
    }

    [Fact]
    public void Get_Missing_ReturnsFalse()
    {
        using var cache = new VideoFrameCache(64L * 1024 * 1024);
        Assert.False(cache.TryGet(123, out _));
    }

    [Fact]
    public void Add_SamePtsTwice_DoesNotDoubleCount()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);
        var frameBytes = FrameBytes(session!);
        Assert.True(session.TryReadNextFrame(out var pts));

        using var cache = new VideoFrameCache(64L * 1024 * 1024);
        cache.Add(pts, session.CurrentFrame, frameBytes);
        cache.Add(pts, session.CurrentFrame, frameBytes);

        Assert.Equal(1, cache.Count);
        Assert.Equal(frameBytes, cache.ByteCount);
    }

    [Fact]
    public void Budget_EvictsLeastRecentlyUsed()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);
        var frameBytes = FrameBytes(session!);

        // Budget for exactly two frames; the third add must evict one.
        using var cache = new VideoFrameCache(2L * frameBytes);

        Assert.True(session.TryReadNextFrame(out var p0));
        cache.Add(p0, session.CurrentFrame, frameBytes);
        Assert.True(session.TryReadNextFrame(out var p1));
        cache.Add(p1, session.CurrentFrame, frameBytes);

        // Touch p0 so p1 becomes the least-recently-used.
        Assert.True(cache.TryGet(p0, out _));

        Assert.True(session.TryReadNextFrame(out var p2));
        cache.Add(p2, session.CurrentFrame, frameBytes);

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet(p0, out _), "p0 was touched and must survive");
        Assert.True(cache.TryGet(p2, out _), "p2 was just added and must survive");
        Assert.False(cache.TryGet(p1, out _), "p1 was least-recently-used and must be evicted");
        Assert.True(cache.ByteCount <= 2L * frameBytes);
    }

    [Fact]
    public void SetBudget_Shrink_EvictsOnNextAdd()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);
        var frameBytes = FrameBytes(session!);

        using var cache = new VideoFrameCache(10L * frameBytes);
        Assert.True(session.TryReadNextFrame(out var p0));
        cache.Add(p0, session.CurrentFrame, frameBytes);
        Assert.True(session.TryReadNextFrame(out var p1));
        cache.Add(p1, session.CurrentFrame, frameBytes);
        Assert.True(session.TryReadNextFrame(out var p2));
        cache.Add(p2, session.CurrentFrame, frameBytes);
        Assert.Equal(3, cache.Count);

        // Shrink to one frame; eviction is applied lazily on the next add (the worker owns the cache).
        cache.SetBudget(frameBytes);
        Assert.True(session.TryReadNextFrame(out var p3));
        cache.Add(p3, session.CurrentFrame, frameBytes);

        Assert.Equal(1, cache.Count);
        Assert.True(cache.ByteCount <= frameBytes);
        Assert.True(cache.TryGet(p3, out _), "the just-added frame must survive");
    }

    private static int FrameBytes(VideoDecoderSession session)
        => Sdcb.FFmpeg.Raw.ffmpeg.av_image_get_buffer_size(session.PixelFormat, session.Width, session.Height, 1);

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

using Sdcb.FFmpeg.Utils;
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
        var rgba = CopyPixels(converter, converter.Convert(session.CurrentFrame));

        Assert.Equal(session.Width * session.Height * converter.BytesPerPixel, rgba.Length);
    }

    [Fact]
    public void Convert_WithTargetSize_ScalesToRequestedSize()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);
        using var converter = new SoftwareFrameConverter(session!.IsHdr);

        Assert.True(session.TryReadNextFrame(out _));
        var frame = converter.Convert(session.CurrentFrame, 123, 45);

        Assert.Equal(123, frame.Width);
        Assert.Equal(45, frame.Height);
        var rgba = CopyPixels(converter, frame);
        Assert.Equal(123 * 45 * converter.BytesPerPixel, rgba.Length);
    }

    [Fact]
    public void Convert_WithTargetSize_FollowsSourceSizeChange()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);
        using var converter = new SoftwareFrameConverter(session!.IsHdr);

        Assert.True(session.TryReadNextFrame(out _));
        converter.Convert(session.CurrentFrame, 123, 45);

        // Same target size and pixel format, but a much smaller source (a renegotiated capture device):
        // the converter must rebuild for the new source instead of reading it with the 720p geometry.
        using var smaller = Sdcb.FFmpeg.Utils.Frame.CreateVideo(64, 32, Sdcb.FFmpeg.Raw.AVPixelFormat.Yuv420p);
        smaller.EnsureBuffer(1);
        FillYuvjGray(smaller, 180);

        var pixels = CopyPixels(converter, converter.Convert(smaller, 123, 45));

        // Limited-range Y=180 is RGB ~191 everywhere; stale geometry would sample outside the small frame.
        for (var offset = 0; offset < pixels.Length; offset += converter.BytesPerPixel)
            Assert.InRange(pixels[offset], 186, 196);
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
        var first = CopyPixels(converter, converter.Convert(session.CurrentFrame));

        // Seek away and back — the full decode→RGBA pipeline must reproduce identical pixels.
        Assert.True(session.SeekAndDecodeTo(later, out _));
        Assert.True(session.SeekAndDecodeTo(early, out _));
        var second = CopyPixels(converter, converter.Convert(session.CurrentFrame));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Convert_DeprecatedYuvjSource_UsesFullRange()
    {
        using var session = VideoDecoderSession.TryOpen(TestAssets.Video720p, VideoPlaybackOptimization.FastSeeking, out _);
        Assert.NotNull(session);
        using var converter = new SoftwareFrameConverter(session!.IsHdr);

        Assert.True(session.TryReadNextFrame(out _));
        var source = session.CurrentFrame;
        Assert.Equal(Sdcb.FFmpeg.Raw.AVPixelFormat.Yuv420p, (Sdcb.FFmpeg.Raw.AVPixelFormat)source.Format);

        // Synthesize a yuvj420p frame (the MJPEG decoder's deprecated full-range output): neutral gray at
        // Y=180, U=V=128. Full-range conversion maps Y→RGB 180; a limited-range misreading would give ~191.
        using var yuvj = Sdcb.FFmpeg.Utils.Frame.CreateVideo(source.Width, source.Height, Sdcb.FFmpeg.Raw.AVPixelFormat.Yuvj420p);
        yuvj.EnsureBuffer(1);
        FillYuvjGray(yuvj, 180);

        var rgba = converter.Convert(yuvj);
        var pixels = new byte[rgba.Width * rgba.Height * converter.BytesPerPixel];
        converter.CopyPixels(rgba, pixels);

        var mid = ((rgba.Height / 2) * rgba.Width + rgba.Width / 2) * converter.BytesPerPixel;
        Assert.InRange(pixels[mid], 176, 184);
        Assert.InRange(pixels[mid + 1], 176, 184);
        Assert.InRange(pixels[mid + 2], 176, 184);
        Assert.Equal(255, pixels[mid + 3]);
    }

    private static unsafe void FillYuvjGray(Sdcb.FFmpeg.Utils.Frame frame, byte y)
    {
        var yPlane = (byte*)frame.Data[0];
        var yStride = frame.Linesize[0];
        for (var row = 0; row < frame.Height; row++)
            System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref System.Runtime.CompilerServices.Unsafe.AsRef<byte>(yPlane + row * yStride), frame.Width).Fill(y);

        var uvWidth = frame.Width / 2;
        var uvHeight = frame.Height / 2;
        for (var plane = 1; plane <= 2; plane++)
        {
            var uvPlane = (byte*)frame.Data[plane];
            var uvStride = frame.Linesize[plane];
            for (var row = 0; row < uvHeight; row++)
                System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref System.Runtime.CompilerServices.Unsafe.AsRef<byte>(uvPlane + row * uvStride), uvWidth).Fill(128);
        }
    }

    // Frame.ToImageBuffer trips Sdcb's Linux marshaling bug (see SoftwareFrameConverter.CopyPixels) —
    // read pixels through the production copy instead.
    private static byte[] CopyPixels(SoftwareFrameConverter converter, Frame rgba)
    {
        var pixels = new byte[rgba.Width * rgba.Height * converter.BytesPerPixel];
        converter.CopyPixels(rgba, pixels);
        return pixels;
    }
}

using System;
using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;
using Xunit;

namespace T3.VideoServices.Tests;

// Verifies the encoder's RGBA→YUV swscale produces a BT.709 (not the swscale-default BT.601) matrix, using the
// same raw sws_setColorspaceDetails config VideoFileEncoder uses. Pure green Y differs: 709≈173, 601≈145 (limited).
public class EncoderColorMatrixTests
{
    [Fact]
    public unsafe void RgbaToYuv_UsesBt709Limited()
    {
        Assert.True(FfmpegLibrary.EnsureInitialized());
        const int w = 16, h = 16;

        using var src = Frame.CreateVideo(w, h, AVPixelFormat.Rgba);
        src.EnsureBuffer(1);
        var sp = src.Data[0]; var ss = src.Linesize[0];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var o = y * ss + x * 4;
            Marshal.WriteByte(sp, o, 0); Marshal.WriteByte(sp, o + 1, 255); Marshal.WriteByte(sp, o + 2, 0); Marshal.WriteByte(sp, o + 3, 255);
        }

        using var dst = Frame.CreateVideo(w, h, AVPixelFormat.Yuv420p);
        dst.EnsureBuffer(1);

        var ctx = ffmpeg.sws_getContext(w, h, AVPixelFormat.Rgba, w, h, AVPixelFormat.Yuv420p, (int)SWS.Bilinear, null, null, null);
        Assert.True(ctx != null);
        var coeff709 = ffmpeg.sws_getCoefficients(1); // SWS_CS_ITU709
        ffmpeg.sws_setColorspaceDetails(ctx, coeff709, 1, coeff709, 0, 0, 1 << 16, 1 << 16);

        var srcData = new byte*[4]; var srcStride = new int[4]; var dstData = new byte*[4]; var dstStride = new int[4];
        for (var i = 0; i < 4; i++) { srcData[i] = (byte*)src.Data[i]; srcStride[i] = src.Linesize[i]; dstData[i] = (byte*)dst.Data[i]; dstStride[i] = dst.Linesize[i]; }
        ffmpeg.sws_scale(ctx, srcData, srcStride, 0, h, dstData, dstStride);
        ffmpeg.sws_freeContext(ctx);

        var yVal = Marshal.ReadByte(dst.Data[0], 0);
        Assert.InRange(yVal, 168, 178); // BT.709 green (601 would be ~145)
    }
}

using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;

namespace T3.VideoServices;

/// <summary>
/// Converts a software-decoded planar YUV frame to packed RGBA on the CPU via swscale — RGBA8 for SDR,
/// RGBA16 (<c>Rgba64le</c>) for HDR/10-bit. The result is uploaded to a GPU texture by the caller. The
/// hardware-decode path instead converts NV12 on the GPU (no CPU round-trip); this is the software fallback.
/// Owned by a single decode worker; the destination frame is reused across calls.
/// </summary>
public sealed class SoftwareFrameConverter : IDisposable
{
    /// <summary>Packed output pixel format: <c>Rgba</c> (SDR) or <c>Rgba64le</c> (HDR).</summary>
    public AVPixelFormat OutputFormat { get; }

    public int BytesPerPixel { get; }

    public SoftwareFrameConverter(bool hdr)
    {
        OutputFormat = hdr ? AVPixelFormat.Rgba64le : AVPixelFormat.Rgba;
        BytesPerPixel = hdr ? 8 : 4;
    }

    /// <summary>
    /// Converts <paramref name="source"/> into a reused packed-RGBA frame and returns it (valid until the
    /// next call). Read pixels via <c>Data[0]</c>/<c>Linesize[0]</c> or <c>ToImageBuffer</c>.
    /// </summary>
    public Frame Convert(Frame source)
    {
        if (_destination == null || _width != source.Width || _height != source.Height)
        {
            _destination?.Dispose();
            _width = source.Width;
            _height = source.Height;
            _destination = Frame.CreateVideo(_width, _height, OutputFormat);
            _destination.EnsureBuffer(1);
        }

        _converter.ConvertFrame(source, _destination, SWS.Bilinear);
        return _destination;
    }

    public void Dispose()
    {
        _destination?.Dispose();
        _converter.Free();
    }

    private readonly VideoFrameConverter _converter = new();
    private Frame? _destination;
    private int _width;
    private int _height;
}

using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;

namespace T3.VideoServices;

/// <summary>
/// Converts a software-decoded planar YUV frame to packed RGBA on the CPU via swscale — RGBA8 for SDR,
/// RGBA16 (<c>Rgba64le</c>) for HDR/10-bit. The result is uploaded to a GPU texture by the caller. The
/// hardware-decode path instead converts NV12 on the GPU (no CPU round-trip); this is the software fallback.
/// Owned by a single decode worker; the destination frame is reused across calls.
/// </summary>
/// <remarks>
/// Swscale is driven directly (a cached <c>SwsContext</c>) instead of through Sdcb's converter wrapper for
/// two reasons: context parameters are cached here instead of being renegotiated per call, and — the actual
/// motivation — MJPEG capture devices decode to the <em>deprecated</em> <c>yuvj*</c> pixel formats, whose
/// only purpose is to imply full/JPEG color range. Swscale warns about them on every use; mapping them to
/// their modern equivalent and stating the range explicitly (<see cref="SetExplicitRanges"/>) is both quiet
/// and unambiguous about the color math.
/// </remarks>
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
        return Convert(source, source.Width, source.Height);
    }

    /// <summary>
    /// Like <see cref="Convert(Frame)"/>, but scales to the requested size (bilinear) in the same swscale
    /// pass — for capture devices that didn't accept the requested resolution and must be resized in software.
    /// </summary>
    public unsafe Frame Convert(Frame source, int targetWidth, int targetHeight)
    {
        var sourceFormat = (AVPixelFormat)source.Format;
        if (_destination == null || _width != targetWidth || _height != targetHeight || _sourceFormat != sourceFormat)
        {
            _destination?.Dispose();
            _width = targetWidth;
            _height = targetHeight;
            _sourceFormat = sourceFormat;
            _destination = Frame.CreateVideo(_width, _height, OutputFormat);
            _destination.EnsureBuffer(1);

            RebuildContext(source);
        }
        else if (_context == null || _sourceWidth != source.Width || _sourceHeight != source.Height)
        {
            // A fixed target size (software resize) hides a source renegotiation from the check above, and a
            // context built for the old source size reads past the new frame's planes.
            RebuildContext(source);
        }

        var srcData = new byte*[4];
        var srcStride = new int[4];
        var dstData = new byte*[4];
        var dstStride = new int[4];
        for (var i = 0; i < 4; i++)
        {
            srcData[i] = (byte*)source.Data[i];
            srcStride[i] = source.Linesize[i];
            dstData[i] = (byte*)_destination.Data[i];
            dstStride[i] = _destination.Linesize[i];
        }

        ffmpeg.sws_scale(_context, srcData, srcStride, 0, source.Height, dstData, dstStride);
        return _destination;
    }

    /// <summary>
    /// Copies the converted frame's packed pixels into <paramref name="destination"/>, which must hold at
    /// least <c>Width * Height * BytesPerPixel</c> bytes.
    /// </summary>
    /// <remarks>
    /// Sdcb's <c>Frame.FillImageBuffer</c> would do the same thing but is unusable outside Windows: its
    /// binding declares FFmpeg's <c>uint8_t *data[4]</c> and <c>int linesize[4]</c> parameters as by-value
    /// structs where the C signature decays them to pointers, so the two sides disagree about argument
    /// layout. The Windows x64 convention happens to hide that; on Linux libavutil receives a garbage
    /// picture height - a different one on every run - and rejects the call. For a packed single-plane
    /// format the copy below is what that helper does anyway, and it saves an interop hop on Windows too.
    /// </remarks>
    public unsafe void CopyPixels(Frame rgba, Span<byte> destination)
    {
        var rowBytes = rgba.Width * BytesPerPixel;
        var stride = rgba.Linesize[0];
        var source = (byte*)rgba.Data[0];

        fixed (byte* target = destination)
        {
            // swscale usually leaves no padding at this width, and then the plane is one contiguous block.
            if (stride == rowBytes)
            {
                var total = (long)rowBytes * rgba.Height;
                Buffer.MemoryCopy(source, target, total, total);
                return;
            }

            for (var y = 0; y < rgba.Height; y++)
            {
                Buffer.MemoryCopy(source + (long)y * stride, target + (long)y * rowBytes, rowBytes, rowBytes);
            }
        }
    }

    // Recreates the swscale context for the current source/target combination. Deprecated yuvj* source
    // formats are mapped to their modern equivalent with the color range stated explicitly, because the
    // deprecated formats exist purely to smuggle the "full range" bit through the pixel format — stating it
    // directly is the migration swscale's warning asks for. MJPEG is the common producer of these (JFIF
    // mandates BT.601 full range), so BT.601 coefficients are the matching matrix.
    private unsafe void RebuildContext(Frame source)
    {
        if (_context != null)
        {
            ffmpeg.sws_freeContext(_context);
            _context = null;
        }

        var swsSourceFormat = _sourceFormat;
        var sourceRange = (int)AVColorRange.Unspecified; // let swscale apply its per-format defaults
        if (_sourceFormat is AVPixelFormat.Yuvj420p or AVPixelFormat.Yuvj422p or AVPixelFormat.Yuvj444p or AVPixelFormat.Yuvj411p)
        {
            swsSourceFormat = _sourceFormat switch
                              {
                                  AVPixelFormat.Yuvj420p => AVPixelFormat.Yuv420p,
                                  AVPixelFormat.Yuvj422p => AVPixelFormat.Yuv422p,
                                  AVPixelFormat.Yuvj444p => AVPixelFormat.Yuv444p,
                                  _ => AVPixelFormat.Yuv411p,
                              };
            sourceRange = (int)AVColorRange.Jpeg;
        }
        else if ((AVColorRange)source.ColorRange == AVColorRange.Jpeg && IsYuvPlanar(_sourceFormat))
        {
            // Non-deprecated formats can still carry full range in metadata (some UVC cameras report YUV420P
            // + JPEG range); honoring it matches what the deprecated formats used to encode.
            sourceRange = (int)AVColorRange.Jpeg;
        }

        _context = ffmpeg.sws_getContext(source.Width, source.Height, swsSourceFormat,
                                         _width, _height, OutputFormat,
                                         (int)SWS.Bilinear, null, null, null);
        if (_context == null)
            throw new InvalidOperationException("sws_getContext failed for the video frame converter");

        _sourceWidth = source.Width;
        _sourceHeight = source.Height;

        if (sourceRange != (int)AVColorRange.Unspecified)
            SetExplicitRanges(sourceRange);
    }

    // Full-range YUV in, full-range RGB out (RGB has no limited variant here), BT.601 coefficients.
    private unsafe void SetExplicitRanges(int sourceRange)
    {
        var coefficients = ffmpeg.sws_getCoefficients(SwsColorspaceSmpte170m); // == ITU-R BT.601
        ffmpeg.sws_setColorspaceDetails(_context, coefficients, sourceRange, coefficients, 1, 0, 1 << 16, 1 << 16);
    }

    private static bool IsYuvPlanar(AVPixelFormat format)
    {
        return format is AVPixelFormat.Yuv420p or AVPixelFormat.Yuv422p or AVPixelFormat.Yuv444p
                       or AVPixelFormat.Yuv411p or AVPixelFormat.Yuv410p;
    }

    public void Dispose()
    {
        _destination?.Dispose();
        unsafe
        {
            if (_context != null)
            {
                ffmpeg.sws_freeContext(_context);
                _context = null;
            }
        }
    }

    // SWS_CS_SMPTE170M — the coefficient set libswscale maps ITU-R BT.601 to (also its own default).
    private const int SwsColorspaceSmpte170m = 5;

    private unsafe SwsContext* _context;
    private Frame? _destination;
    private AVPixelFormat _sourceFormat;
    private int _sourceWidth;
    private int _sourceHeight;
    private int _width;
    private int _height;
}

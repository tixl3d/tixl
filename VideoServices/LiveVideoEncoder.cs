using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;
using T3.Core.Logging;
using T3.Core.Video;

namespace T3.VideoServices;

/// <summary>
/// The FFmpeg implementation of <see cref="IVideoLiveEncoderFactory"/>. Registered next to the file-export
/// factory so the editor can encode a live H.264 stream without referencing this assembly.
/// </summary>
public sealed class FfmpegLiveEncoderFactory : IVideoLiveEncoderFactory
{
    public static void Register() => VideoExport.LiveFactory ??= new FfmpegLiveEncoderFactory();

    public ILiveVideoEncoder? TryCreateLiveEncoder(VideoLiveEncoderSettings settings, out string? error)
    {
        error = null;
        try
        {
            return new LiveH264Encoder(settings);
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }
}

/// <summary>
/// Encodes RGBA frames into Annex-B H.264 access units for WebRTC. Unlike the file encoder there is no
/// muxer and no container: every packet the codec emits is handed to the caller, with SPS/PPS in-band
/// before each IDR (the encoder is opened without the global-header flag on purpose).
///
/// Not thread-safe: one encoder is driven by one caller.
/// </summary>
public sealed class LiveH264Encoder : ILiveVideoEncoder
{
    public string EncoderName => _encoderName;

    public event Action<byte[]>? AccessUnitEncoded;

    public unsafe LiveH264Encoder(in VideoLiveEncoderSettings settings)
    {
        if (!FfmpegLibrary.EnsureInitialized())
            throw new InvalidOperationException(FfmpegLibrary.StatusError ?? "FFmpeg is not available");

        var (codec, encoderName) = FindH264Encoder();
        _encoderName = FriendlyName(encoderName);
        _settings = settings;

        var frameRate = Math.Max(1, (int)Math.Round(settings.FrameRate));
        var pixelFormat = HardwareEncoderProbe.EncoderInputFormat(encoderName);
        var bitRate = settings.BitRate > 0 ? settings.BitRate : (long)settings.Width * settings.Height * 4;

        _codecContext = new CodecContext(codec)
                            {
                                Width = settings.Width,
                                Height = settings.Height,
                                TimeBase = new AVRational(1, frameRate),
                                Framerate = new AVRational(frameRate, 1),
                                PixelFormat = pixelFormat,
                                BitRate = bitRate,
                                // Two seconds between IDRs bounds recovery time for a viewer that joins or
                                // loses packets without making every frame a keyframe.
                                GopSize = frameRate * 2,
                                MaxBFrames = 0,
                                ColorRange = AVColorRange.Mpeg,
                                ColorPrimaries = AVColorPrimaries.Bt709,
                                ColorTrc = AVColorTransferCharacteristic.Bt709,
                                Colorspace = AVColorSpace.Bt709,
                                ThreadCount = 0,
                            };

        // Low-delay is the one flag every H.264 encoder understands; the private options below vary by
        // encoder and are only set where the name guarantees them.
        _codecContext.Flags |= AV_CODEC_FLAG.LowDelay;
        ApplyLowLatencyOptions(_codecContext, encoderName);

        try
        {
            _codecContext.Open();
        }
        catch
        {
            _codecContext.Dispose();
            throw;
        }

        _encoderFrame = Frame.CreateVideo(settings.Width, settings.Height, pixelFormat);
        _encoderFrame.EnsureBuffer(1);
        _encoderFrame.ColorRange = AVColorRange.Mpeg;
        _encoderFrame.ColorPrimaries = AVColorPrimaries.Bt709;
        _encoderFrame.ColorTrc = AVColorTransferCharacteristic.Bt709;
        _encoderFrame.Colorspace = AVColorSpace.Bt709;

        EnsureSourceSize(settings.Width, settings.Height);
    }

    public void Encode(ReadOnlySpan<byte> rgbaPixels, int rowStride, bool forceKeyFrame) =>
        EncodeScaled(rgbaPixels, rowStride, _settings.Width, _settings.Height, forceKeyFrame);

    public unsafe void EncodeScaled(ReadOnlySpan<byte> rgbaPixels, int rowStride, int sourceWidth, int sourceHeight,
                                    bool forceKeyFrame)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LiveH264Encoder));

        EnsureSourceSize(sourceWidth, sourceHeight);

        var rowBytes = sourceWidth * 4;
        var destStride = _sourceFrame!.Linesize[0];
        var dest = (byte*)_sourceFrame.Data[0];
        fixed (byte* src = rgbaPixels)
        {
            for (var y = 0; y < sourceHeight; y++)
                Buffer.MemoryCopy(src + (long)y * rowStride, dest + (long)y * destStride, destStride, rowBytes);
        }

        // Same reason as the file encoder: the codec keeps reading the frame while we scale the next one.
        ffmpeg.av_frame_make_writable(_encoderFrame);
        ScaleSourceFrame(sourceHeight);

        _encoderFrame.Pts = _nextPts++;
        // Encoders honor a requested picture type by starting a new GOP with an IDR here.
        _encoderFrame.PictType = forceKeyFrame ? AVPictureType.I : AVPictureType.None;

        _codecContext.SendFrame(_encoderFrame);
        DrainPackets();
    }

    private unsafe void ScaleSourceFrame(int sourceHeight)
    {
        var srcData = new byte*[4];
        var srcStride = new int[4];
        var dstData = new byte*[4];
        var dstStride = new int[4];
        for (var i = 0; i < 4; i++)
        {
            srcData[i] = (byte*)_sourceFrame!.Data[i];
            srcStride[i] = _sourceFrame.Linesize[i];
            dstData[i] = (byte*)_encoderFrame.Data[i];
            dstStride[i] = _encoderFrame.Linesize[i];
        }

        ffmpeg.sws_scale(_swsContext, srcData, srcStride, 0, sourceHeight, dstData, dstStride);
    }

    /// <summary>Recreates the source frame and the scaler when the render target resolution changes.</summary>
    private unsafe void EnsureSourceSize(int width, int height)
    {
        if (_sourceFrame != null && _sourceWidth == width && _sourceHeight == height)
            return;

        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }

        _sourceFrame?.Dispose();
        _sourceWidth = width;
        _sourceHeight = height;

        _sourceFrame = Frame.CreateVideo(width, height, AVPixelFormat.Rgba);
        _sourceFrame.EnsureBuffer(1);
        _sourceFrame.ColorRange = AVColorRange.Jpeg;

        var pixelFormat = (AVPixelFormat)_encoderFrame.Format;
        _swsContext = ffmpeg.sws_getContext(width, height, AVPixelFormat.Rgba,
                                            _settings.Width, _settings.Height, pixelFormat,
                                            (int)SWS.Bilinear, null, null, null);
        if (_swsContext == null)
            throw new InvalidOperationException("sws_getContext failed");

        var coeff709 = ffmpeg.sws_getCoefficients(1); // SWS_CS_ITU709
        // full-range RGB in (1) → limited-range YUV out (0), BT.709 coefficients.
        ffmpeg.sws_setColorspaceDetails(_swsContext, coeff709, 1, coeff709, 0, 0, 1 << 16, 1 << 16);
    }

    private void DrainPackets()
    {
        while (_codecContext.ReceivePacket(_packet) == CodecResult.Success)
        {
            var data = _packet.Data.ToArray();
            _packet.Unref();
            if (data.Length > 0)
                AccessUnitEncoded?.Invoke(data);
        }
    }

    private static (Codec Codec, string Name) FindH264Encoder()
    {
        var hardware = HardwareEncoderProbe.H264HardwareEncoder;
        if (!string.IsNullOrEmpty(hardware))
        {
            if (TryResolve(hardware, out var hardwareCodec))
                return (hardwareCodec, hardware);
        }

        // The shipped FFmpeg is LGPL: software H.264 is OpenH264, never libx264.
        if (TryResolve("libopenh264", out var software))
            return (software, "libopenh264");

        throw new InvalidOperationException("No H.264 encoder is available (no hardware encoder and no OpenH264)");
    }

    private static bool TryResolve(string name, out Codec codec)
    {
        try
        {
            var found = Codec.FindEncoderByName(name);
            if (found != null)
            {
                codec = found.Value;
                return true;
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Live video: encoder '{name}' is not available: {e.Message}");
        }

        codec = default;
        return false;
    }

    internal static void ApplyLowLatencyOptions(CodecContext context, string encoderName)
    {
        if (encoderName.Contains("_videotoolbox", StringComparison.OrdinalIgnoreCase))
        {
            // VideoToolbox buffers frames for lookahead by default, so a short live stream can produce no
            // packets at all. Realtime trades compression efficiency for immediate output and lets the
            // forced keyframe come out as an IDR.
            context.Options.Set("realtime", "1");
            return;
        }

        if (!encoderName.Contains("_nvenc", StringComparison.OrdinalIgnoreCase))
            return;

        // NVENC's universal low-latency preset; CBR keeps the rate steady for a call-like stream.
        context.Options.Set("preset", "p1");
        context.Options.Set("tune", "ull");
        context.Options.Set("zerolatency", "1");
        context.Options.Set("delay", "0");
        context.Options.Set("rc", "cbr");
    }

    private static string FriendlyName(string encoderName) =>
        encoderName switch
        {
            "h264_nvenc" => "NVIDIA NVENC",
            "h264_qsv" => "Intel Quick Sync",
            "h264_amf" => "AMD AMF",
            "h264_videotoolbox" => "Apple VideoToolbox",
            "libopenh264" => "OpenH264",
            _ => encoderName,
        };

    public unsafe void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            ffmpeg.avcodec_send_frame(_codecContext, null);
            DrainPackets();
        }
        catch (Exception e)
        {
            Log.Debug($"Live video: encoder flush failed: {e.Message}");
        }

        _packet.Dispose();
        _encoderFrame.Dispose();
        _sourceFrame?.Dispose();
        if (_swsContext != null)
            ffmpeg.sws_freeContext(_swsContext);

        _codecContext.Dispose();
        _swsContext = null;
    }

    private readonly VideoLiveEncoderSettings _settings;
    private readonly string _encoderName;
    private readonly CodecContext _codecContext;
    private readonly Frame _encoderFrame;
    private Frame? _sourceFrame;
    private int _sourceWidth;
    private int _sourceHeight;
    private readonly Packet _packet = new();
    private unsafe SwsContext* _swsContext;
    private long _nextPts;
    private bool _disposed;
}

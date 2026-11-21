using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;
using T3.Core.Logging;
using T3.Core.Video;

namespace T3.VideoServices;

/// <summary>The FFmpeg implementation of <see cref="IVideoLiveDecoderFactory"/>, registered by the video package.</summary>
public sealed class FfmpegLiveDecoderFactory : IVideoLiveDecoderFactory
{
    public static void Register() => VideoExport.LiveDecoderFactory ??= new FfmpegLiveDecoderFactory();

    public ILiveVideoDecoder? TryCreateLiveVideoDecoder(out string? error)
    {
        error = null;
        try
        {
            return new LiveH264Decoder();
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }
}

/// <summary>
/// Decodes the Annex-B H.264 access units of a live stream into a reused RGBA buffer. No demuxer and
/// no seeking: each call feeds one access unit and returns whatever frame the decoder emits. Not
/// thread-safe; one decoder is driven by one consumer.
/// </summary>
public sealed class LiveH264Decoder : ILiveVideoDecoder
{
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>The last decoded frame as packed RGBA8, valid until the next decode call.</summary>
    public ReadOnlySpan<byte> Rgba => _rgba.AsSpan(0, Width * Height * 4);

    public unsafe LiveH264Decoder()
    {
        if (!FfmpegLibrary.EnsureInitialized())
            throw new InvalidOperationException(FfmpegLibrary.StatusError ?? "FFmpeg is not available");

        var codec = Codec.FindDecoderById(AVCodecID.H264);
        _codecContext = new CodecContext(codec)
                            {
                                TimeBase = new AVRational(1, 90_000),
                                ThreadCount = 0,
                            };
        _codecContext.Open();
        _converter = new SoftwareFrameConverter(hdr: false);
    }

    /// <summary>
    /// Feeds one Annex-B access unit. Returns true when a complete frame is available in <see cref="Rgba"/>.
    /// </summary>
    public unsafe bool TryDecode(ReadOnlySpan<byte> annexB)
    {
        if (_disposed || annexB.Length == 0)
            return false;

        using var packet = new Packet();
        if (ffmpeg.av_new_packet(packet, annexB.Length) < 0)
            return false;

        // av_new_packet allocates the buffer FFmpeg owns; Packet.FromData would make FFmpeg free a
        // pinned managed array instead.
        annexB.CopyTo(packet.Data.AsSpan());
        packet.Pts = _pts++;

        // Raw call for the same reason as the file decoder: a fragmentary first packet is skippable,
        // while Sdcb's wrapper throws on any negative result.
        var sendResult = ffmpeg.avcodec_send_packet(_codecContext, packet);
        if (sendResult < 0 && sendResult != AvErrorInvalidData)
        {
            Log.Debug($"Live video: decoder rejected a packet ({sendResult})");
            return false;
        }

        var produced = false;
        while (_codecContext.ReceiveFrame(_frame) == CodecResult.Success)
        {
            CopyFrameToRgba();
            produced = true;
        }

        return produced;
    }

    /// <summary>
    /// H.264 decoders hold the newest frame until the next slice header or a flush; a stream that
    /// just stopped would otherwise lose it.
    /// </summary>
    public unsafe void Flush()
    {
        if (_disposed)
            return;

        ffmpeg.avcodec_send_packet(_codecContext, null);
        while (_codecContext.ReceiveFrame(_frame) == CodecResult.Success)
            CopyFrameToRgba();
    }

    private unsafe void CopyFrameToRgba()
    {
        var converted = _converter.Convert(_frame);
        var width = converted.Width;
        var height = converted.Height;
        var stride = converted.Linesize[0];
        var required = width * height * 4;

        if (_rgba.Length < required || Width != width || Height != height)
        {
            Width = width;
            Height = height;
            _rgba = new byte[required];
        }

        fixed (byte* destination = _rgba)
        {
            var source = (byte*)converted.Data[0];
            for (var y = 0; y < height; y++)
                Buffer.MemoryCopy(source + (long)y * stride, destination + (long)y * width * 4, width * 4, width * 4);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _converter.Dispose();
        _frame.Dispose();
        _codecContext.Dispose();
    }

    private const int AvErrorInvalidData = -1094995529;

    private readonly CodecContext _codecContext;
    private readonly SoftwareFrameConverter _converter;
    private readonly Frame _frame = new();
    private byte[] _rgba = [];
    private long _pts;
    private bool _disposed;
}

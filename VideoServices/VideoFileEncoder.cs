using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;
using T3.Core.Logging;

namespace T3.VideoServices;

/// <summary>
/// Settings for one <see cref="VideoFileEncoder"/>. Pixel/codec types are FFmpeg-level on purpose — the
/// editor-facing Core facade maps its own FFmpeg-free settings onto these.
/// </summary>
public readonly record struct VideoEncoderSettings
{
    public required string FilePath { get; init; }

    /// <summary>Muxer name (e.g. <c>mp4</c>); null infers it from <see cref="FilePath"/>'s extension.</summary>
    public string? FormatName { get; init; }

    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Frames per second as a rational (e.g. <c>60/1</c>, <c>60000/1001</c>).</summary>
    public required AVRational FrameRate { get; init; }

    public long BitRate { get; init; }

    /// <summary>
    /// Explicit encoder name (e.g. <c>h264_nvenc</c>) — wins over <see cref="VideoCodecId"/> when set. Lets the
    /// caller target a specific hardware encoder; null falls back to the codec id's default encoder.
    /// </summary>
    public string? VideoEncoderName { get; init; }

    /// <summary>Used when <see cref="VideoEncoderName"/> is null (e.g. <c>Mpeg4</c> for the LGPL fallback).</summary>
    public AVCodecID VideoCodecId { get; init; }

    /// <summary>Codec-private AVOptions applied when the encoder is opened (e.g. HAP's <c>format</c> =
    /// <c>hap_q</c>). Optional; null applies none.</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? VideoCodecOptions { get; init; }

    /// <summary>
    /// Pixel format fed to the encoder; the swscale step converts <see cref="SourceFormat"/> into it, so it must be
    /// one the encoder accepts (e.g. <c>Yuv420p</c> for H.264/MPEG-4, <c>Yuv422p10le</c> for ProRes). Defaults to
    /// <c>Yuv420p</c> (the enum's zero value); <see cref="AVPixelFormat.None"/> is treated the same.
    /// </summary>
    public AVPixelFormat EncoderPixelFormat { get; init; }

    /// <summary>Pixel format of the bytes passed to <see cref="VideoFileEncoder.WriteVideoFrame"/> (e.g. <c>Rgba</c>).</summary>
    public AVPixelFormat SourceFormat { get; init; }

    /// <summary>Bytes per pixel of <see cref="SourceFormat"/> (e.g. 4 for RGBA8).</summary>
    public int SourceBytesPerPixel { get; init; }

    /// <summary>When true, an AAC audio stream is muxed in; feed it interleaved-float PCM via
    /// <see cref="VideoFileEncoder.WriteAudioSamples"/>.</summary>
    public bool EncodeAudio { get; init; }

    public int AudioSampleRate { get; init; }
    public int AudioChannels { get; init; }

    /// <summary>AAC target bit rate; ≤ 0 uses a 192 kbit default.</summary>
    public long AudioBitRate { get; init; }
}

/// <summary>
/// Encodes a sequence of CPU frames (plus optional audio) into a muxed video file via FFmpeg (libav*), for
/// render-export. CPU-byte level on purpose: the GPU texture read-back stays in the caller, so this is
/// unit-testable without a D3D device. The video codec is caller-chosen — a hardware H.264 encoder
/// (<c>h264_nvenc</c>/<c>_qsv</c>/<c>_amf</c>) on capable machines, or a software LGPL codec otherwise
/// (software H.264 is libx264 = GPL and absent from the bundled LGPL build). Audio is native AAC (LGPL).
///
/// Not thread-safe: one encoder is driven by one caller. Per frame, call <see cref="WriteVideoFrame"/> and (if
/// audio) <see cref="WriteAudioSamples"/>, then <see cref="Finish"/> (or <see cref="Dispose"/>) exactly once.
/// </summary>
public sealed class VideoFileEncoder : IDisposable
{
    // FFmpeg's native AAC encoder consumes planar float.
    private const AVSampleFormat AudioSampleFormat = AVSampleFormat.Fltp;

    public unsafe VideoFileEncoder(in VideoEncoderSettings settings)
    {
        if (!FfmpegLibrary.EnsureInitialized())
            throw new InvalidOperationException(FfmpegLibrary.StatusError ?? "FFmpeg is not available");

        _settings = settings;

        // None (and the struct's zero default) means "let the encoder use planar 4:2:0".
        var encoderPixelFormat = settings.EncoderPixelFormat == AVPixelFormat.None
                                     ? AVPixelFormat.Yuv420p
                                     : settings.EncoderPixelFormat;

        var videoCodec = !string.IsNullOrEmpty(settings.VideoEncoderName)
                             ? Codec.FindEncoderByName(settings.VideoEncoderName)
                             : Codec.FindEncoderById(settings.VideoCodecId);

        _formatContext = FormatContext.AllocOutput(formatName: settings.FormatName, fileName: settings.FilePath);

        // Some muxers (mp4/mov) keep codec headers in the container, not the bitstream — required for a playable file.
        var outputFormat = _formatContext.OutputFormat;
        var needsGlobalHeader = outputFormat != null && outputFormat.Value.Flags.HasFlag(AVFMT.Globalheader);

        _videoStream = _formatContext.NewStream(videoCodec);
        _videoCodecContext = new CodecContext(videoCodec)
                                 {
                                     Width = settings.Width,
                                     Height = settings.Height,
                                     TimeBase = new AVRational(settings.FrameRate.Den, settings.FrameRate.Num),
                                     Framerate = settings.FrameRate,
                                     PixelFormat = encoderPixelFormat,
                                     BitRate = settings.BitRate,
                                     // Tag BT.709 / limited range, matching the previous Media Foundation output, so the
                                     // file isn't untagged (players then guess BT.601 vs 709 and the colour shifts).
                                     ColorRange = AVColorRange.Mpeg,
                                     ColorPrimaries = AVColorPrimaries.Bt709,
                                     ColorTrc = AVColorTransferCharacteristic.Bt709,
                                     Colorspace = AVColorSpace.Bt709,
                                     // 0 = auto-select thread count; lets the software encoders (VP9/AV1/kvazaar/FFV1) use
                                     // all cores instead of one. Hardware encoders ignore it.
                                     ThreadCount = 0,
                                 };
        if (needsGlobalHeader)
            _videoCodecContext.Flags |= AV_CODEC_FLAG.GlobalHeader;
        OpenVideoCodec(settings.VideoCodecOptions);
        _videoStream.Codecpar!.CopyFrom(_videoCodecContext);
        _videoStream.TimeBase = _videoCodecContext.TimeBase;

        if (settings.EncodeAudio)
            SetupAudioStream(settings, needsGlobalHeader);

        // All streams added — now open the file and write the header (must precede any packet).
        _ioContext = IOContext.OpenWrite(settings.FilePath, null);
        _formatContext.Pb = _ioContext;
        _formatContext.WriteHeader(null);

        _sourceFrame = Frame.CreateVideo(settings.Width, settings.Height, settings.SourceFormat);
        _sourceFrame.EnsureBuffer(1);
        _sourceFrame.ColorRange = AVColorRange.Jpeg; // source RGB is full-range

        _encoderFrame = Frame.CreateVideo(settings.Width, settings.Height, encoderPixelFormat);
        _encoderFrame.EnsureBuffer(1);
        _encoderFrame.ColorRange = AVColorRange.Mpeg;
        _encoderFrame.ColorPrimaries = AVColorPrimaries.Bt709;
        _encoderFrame.ColorTrc = AVColorTransferCharacteristic.Bt709;
        _encoderFrame.Colorspace = AVColorSpace.Bt709;

        // Drive swscale directly so we can force BT.709: its RGB→YUV default is BT.601, and Sdcb's
        // VideoFrameConverter doesn't expose the colorspace. (No-op for HAP's RGBA→RGBA, where there's no matrix.)
        _swsContext = ffmpeg.sws_getContext(settings.Width, settings.Height, settings.SourceFormat,
                                            settings.Width, settings.Height, encoderPixelFormat,
                                            (int)SWS.Bilinear, null, null, null);
        if (_swsContext == null)
            throw new InvalidOperationException("sws_getContext failed");

        if (encoderPixelFormat != settings.SourceFormat)
        {
            var coeff709 = ffmpeg.sws_getCoefficients(1); // SWS_CS_ITU709
            // full-range RGB in (1) → limited-range YUV out (0), BT.709 coefficients.
            ffmpeg.sws_setColorspaceDetails(_swsContext, coeff709, 1, coeff709, 0, 0, 1 << 16, 1 << 16);
        }
    }

    // Opens the video encoder, applying any codec-private AVOptions (e.g. HAP's "format") first. Children search
    // descends into the codec's private data, where these options live (mirrors the CLI's `-format hap_q`).
    private void OpenVideoCodec(IReadOnlyList<KeyValuePair<string, string>>? options)
    {
        if (options != null)
        {
            foreach (var (key, value) in options)
                _videoCodecContext.Options.Set(key, value, AV_OPT_SEARCH.Children);
        }

        _videoCodecContext.Open();
    }

    private unsafe void SetupAudioStream(in VideoEncoderSettings settings, bool needsGlobalHeader)
    {
        var audioCodec = Codec.FindEncoderById(AVCodecID.Aac);
        _audioStream = _formatContext.NewStream(audioCodec);

        AVChannelLayout layout = default;
        ffmpeg.av_channel_layout_default(&layout, settings.AudioChannels);
        _audioLayout = layout;

        _audioCodecContext = new CodecContext(audioCodec)
                                 {
                                     SampleRate = settings.AudioSampleRate,
                                     SampleFormat = AudioSampleFormat,
                                     ChLayout = layout,
                                     BitRate = settings.AudioBitRate > 0 ? settings.AudioBitRate : 192_000,
                                     TimeBase = new AVRational(1, settings.AudioSampleRate),
                                 };
        if (needsGlobalHeader)
            _audioCodecContext.Flags |= AV_CODEC_FLAG.GlobalHeader;
        _audioCodecContext.Open();
        _audioStream.Codecpar!.CopyFrom(_audioCodecContext);
        _audioStream.TimeBase = _audioCodecContext.TimeBase;

        // AAC fixes its frame size (1024 samples); buffer incoming PCM and emit exactly that many per frame.
        _audioFrameSize = _audioCodecContext.FrameSize > 0 ? _audioCodecContext.FrameSize : 1024;
        _audioFrame = Frame.CreateAudio(AudioSampleFormat, layout, settings.AudioSampleRate, _audioFrameSize);
        _audioFrame.EnsureBuffer(0);
    }

    /// <summary>
    /// Encodes one video frame. <paramref name="sourcePixels"/> holds <see cref="VideoEncoderSettings.Height"/>
    /// rows of <c>Width * SourceBytesPerPixel</c> bytes in <see cref="VideoEncoderSettings.SourceFormat"/>, with
    /// <paramref name="sourceStride"/> bytes per row (≥ the row's pixel bytes; padding ignored).
    /// </summary>
    public unsafe void WriteVideoFrame(ReadOnlySpan<byte> sourcePixels, int sourceStride)
    {
        if (_finished)
            throw new InvalidOperationException("Encoder already finished");

        var rowBytes = _settings.Width * _settings.SourceBytesPerPixel;
        var destStride = _sourceFrame.Linesize[0];
        var dest = (byte*)_sourceFrame.Data[0];
        fixed (byte* src = sourcePixels)
        {
            for (var y = 0; y < _settings.Height; y++)
                Buffer.MemoryCopy(src + (long)y * sourceStride, dest + (long)y * destStride, destStride, rowBytes);
        }

        // SendFrame only takes a *reference* to the frame's buffer, and the encoder's worker threads keep reading
        // it after the call returns. Scaling into the same buffer for the next frame would overwrite slices the
        // encoder is still consuming — a torn picture mixing two frames. Clones only while the buffer is shared.
        ffmpeg.av_frame_make_writable(_encoderFrame);

        ScaleSourceFrame();
        _encoderFrame.Pts = _nextVideoPts++;
        _videoCodecContext.SendFrame(_encoderFrame);
        DrainEncoderToMuxer(_videoCodecContext, _videoStream);
    }

    // RGBA → encoder pixel format via our BT.709-configured swscale context.
    private unsafe void ScaleSourceFrame()
    {
        var srcData = new byte*[4];
        var srcStride = new int[4];
        var dstData = new byte*[4];
        var dstStride = new int[4];
        for (var i = 0; i < 4; i++)
        {
            srcData[i] = (byte*)_sourceFrame.Data[i];
            srcStride[i] = _sourceFrame.Linesize[i];
            dstData[i] = (byte*)_encoderFrame.Data[i];
            dstStride[i] = _encoderFrame.Linesize[i];
        }

        ffmpeg.sws_scale(_swsContext, srcData, srcStride, 0, _settings.Height, dstData, dstStride);
    }

    /// <summary>
    /// Buffers and encodes audio. <paramref name="interleavedFloatPcm"/> is interleaved 32-bit-float samples
    /// (channel-interleaved) matching <see cref="VideoEncoderSettings.AudioChannels"/> /
    /// <see cref="VideoEncoderSettings.AudioSampleRate"/>. No-op when the encoder has no audio stream.
    /// </summary>
    public void WriteAudioSamples(ReadOnlySpan<byte> interleavedFloatPcm)
    {
        if (_audioCodecContext == null)
            return;
        if (_finished)
            throw new InvalidOperationException("Encoder already finished");

        _audioBuffer.AddRange(MemoryMarshal.Cast<byte, float>(interleavedFloatPcm));

        var channels = _settings.AudioChannels;
        var floatsPerFrame = _audioFrameSize * channels;
        var consumedFloats = 0;
        while (_audioBuffer.Count - consumedFloats >= floatsPerFrame)
        {
            EncodeAudioFrame(consumedFloats, _audioFrameSize);
            consumedFloats += floatsPerFrame;
        }

        if (consumedFloats > 0)
            _audioBuffer.RemoveRange(0, consumedFloats);
    }

    /// <summary>Flushes both encoders and writes the container trailer. Idempotent; also called by <see cref="Dispose"/>.</summary>
    public unsafe void Finish()
    {
        if (_finished)
            return;

        _finished = true;

        if (_audioCodecContext != null)
        {
            var remainingSamples = _audioBuffer.Count / _settings.AudioChannels;
            if (remainingSamples > 0)
                EncodeAudioFrame(0, remainingSamples);
            _audioBuffer.Clear();

            ffmpeg.avcodec_send_frame(_audioCodecContext, null); // drain the audio encoder
            DrainEncoderToMuxer(_audioCodecContext, _audioStream);
        }

        ffmpeg.avcodec_send_frame(_videoCodecContext, null); // drain the video encoder
        DrainEncoderToMuxer(_videoCodecContext, _videoStream);

        _formatContext.WriteTrailer();
    }

    // Deinterleaves nbSamples from _audioBuffer (starting at floatOffset) into the planar frame, then encodes it.
    private unsafe void EncodeAudioFrame(int floatOffset, int nbSamples)
    {
        ffmpeg.av_frame_make_writable(_audioFrame!);
        _audioFrame!.NbSamples = nbSamples;

        var channels = _settings.AudioChannels;
        for (var ch = 0; ch < channels; ch++)
        {
            var plane = (float*)_audioFrame.Data[ch];
            for (var i = 0; i < nbSamples; i++)
                plane[i] = _audioBuffer[floatOffset + i * channels + ch];
        }

        _audioFrame.Pts = _audioSamplesEncoded;
        _audioSamplesEncoded += nbSamples;
        _audioCodecContext!.SendFrame(_audioFrame);
        DrainEncoderToMuxer(_audioCodecContext, _audioStream);
    }

    private unsafe void DrainEncoderToMuxer(CodecContext codecContext, MediaStream stream)
    {
        while (codecContext.ReceivePacket(_packet) == CodecResult.Success)
        {
            _packet.StreamIndex = stream.Index;
            ffmpeg.av_packet_rescale_ts(_packet, codecContext.TimeBase, stream.TimeBase);
            _formatContext.InterleavedWritePacket(_packet);
            _packet.Unref();
        }
    }

    public unsafe void Dispose()
    {
        try
        {
            Finish();
        }
        catch (Exception e)
        {
            Log.Warning($"VideoFileEncoder: finishing the output failed - {e.Message}");
        }

        if (_swsContext != null)
            ffmpeg.sws_freeContext(_swsContext);
        _sourceFrame.Dispose();
        _encoderFrame.Dispose();
        _audioFrame?.Dispose();
        _packet.Dispose();
        _videoCodecContext.Dispose();
        _audioCodecContext?.Dispose();
        _formatContext.Dispose();
        _ioContext.Dispose();
    }

    private readonly VideoEncoderSettings _settings;
    private readonly FormatContext _formatContext;
    private readonly IOContext _ioContext;

    private readonly MediaStream _videoStream;
    private readonly CodecContext _videoCodecContext;
    private readonly unsafe SwsContext* _swsContext;
    private readonly Frame _sourceFrame;
    private readonly Frame _encoderFrame;
    private long _nextVideoPts;

    private MediaStream _audioStream;
    private CodecContext? _audioCodecContext;
    private Frame? _audioFrame;
    private AVChannelLayout _audioLayout;
    private int _audioFrameSize;
    private long _audioSamplesEncoded;
    private readonly List<float> _audioBuffer = new();

    private readonly Packet _packet = new();
    private bool _finished;
}

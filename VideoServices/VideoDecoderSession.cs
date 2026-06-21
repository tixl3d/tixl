using System.Runtime.InteropServices;
using System.Threading;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;
using T3.Core.Logging;
using T3.Core.Resource;
using T3.Core.Video;

namespace T3.VideoServices;

/// <summary>
/// Wraps one open video file: its demuxer (<see cref="FormatContext"/>) and decoder
/// (<see cref="CodecContext"/>). Provides the two access patterns the playback controller needs —
/// <see cref="TryReadNextFrame"/> for the fast sequential stream (forward play / export) and
/// <see cref="SeekToKeyframeBefore"/> + decode-forward for exact, frame-accurate seeking.
///
/// Decode runs on the CPU (software) by default. When the D3D11VA hardware path is requested it decodes on
/// the GPU and — in this first hardware milestone — reads the surface back to CPU memory so the existing
/// software converter is reused (the zero-copy GPU convert is a later step). Either way <see cref="CurrentFrame"/>
/// exposes a CPU frame, so the controller is unaffected by the decode backend.
///
/// Not thread-safe: a session is owned by exactly one decode worker thread. FFmpeg's contexts are not
/// reentrant, so single ownership avoids locking them.
/// </summary>
public sealed class VideoDecoderSession : IDisposable
{
    public double DurationSeconds { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// The pixel format of <see cref="CurrentFrame"/> on the CPU (e.g. <c>Nv12</c>, <c>Yuv420p</c>,
    /// <c>P010le</c>). In hardware mode this is the read-back (software) format, not <c>D3d11</c>.
    /// </summary>
    public AVPixelFormat PixelFormat { get; }

    /// <summary>True for PQ/HLG transfer or ≥10-bit formats — the converter should target RGBA16.</summary>
    public bool IsHdr { get; }

    /// <summary>True when decoding runs through the D3D11VA hardware path (with CPU read-back in this milestone).</summary>
    public bool UsesHardwareDecode { get; }

    /// <summary>True when the decoded surface stays on the GPU (no read-back); the caller converts it on the GPU.</summary>
    public bool UsesZeroCopy { get; }

    public int TimeBaseNum { get; }
    public int TimeBaseDen { get; }

    /// <summary>The stream's <c>start_time</c> in time-base ticks (0 when unset).</summary>
    public long StreamStartPts { get; }

    /// <summary>Nominal average frame rate (fps); 0 when unknown.</summary>
    public double FrameRate { get; }

    /// <summary>
    /// The most recently decoded frame as CPU planes; valid after <see cref="TryReadNextFrame"/> returned
    /// true, until the next read. In hardware mode this is the read-back frame. Backends read its
    /// <c>Data</c>/<c>Linesize</c> before advancing.
    /// </summary>
    public Frame CurrentFrame => _lastFrameReadBack ? _swFrame : _frame;

    /// <summary>
    /// Opens <paramref name="url"/> (file or network stream) and selects the best video stream. Returns null
    /// on failure. <paramref name="demuxerOptions"/> passes demuxer options (e.g. <c>rtsp_transport=tcp</c>).
    /// </summary>
    public static unsafe VideoDecoderSession? TryOpen(string url, VideoPlaybackOptimization optimization, out string? error,
                                                      IReadOnlyDictionary<string, string>? demuxerOptions = null)
    {
        error = null;
        if (!FfmpegLibrary.EnsureInitialized())
        {
            error = FfmpegLibrary.StatusError ?? "FFmpeg is not available";
            return null;
        }

        FormatContext? formatContext = null;
        MediaDictionary? options = null;
        try
        {
            if (demuxerOptions != null)
            {
                options = new MediaDictionary();
                foreach (var pair in demuxerOptions)
                    options[pair.Key] = pair.Value;
            }

            formatContext = FormatContext.OpenInputUrl(url, null, options);
            formatContext.LoadStreamInfo();

            var stream = formatContext.FindBestStreamOrNull(AVMediaType.Video);
            if (stream == null)
            {
                error = "No video stream found in " + url;
                formatContext.Dispose();
                return null;
            }

            var videoStream = stream.Value;
            var codecParameters = videoStream.Codecpar;
            if (codecParameters == null)
            {
                error = "Video stream has no codec parameters: " + url;
                formatContext.Dispose();
                return null;
            }

            var codec = Codec.FindDecoderById(codecParameters.CodecId);

            // Playback-performance decodes on the GPU (zero-copy below). Fast-seeking decodes in software: it keeps
            // the GPU free for the editor and avoids a per-frame GPU→CPU read-back stall (the readback path syncs
            // every frame and stutters), while caching the CPU frames for cheap re-seeks. Hardware is the fallback
            // target only for playback-performance; fast-seeking is already software.
            //
            // Only prefer hardware for codecs the decoder can actually decode on D3D11VA. A decoder without a
            // D3D11VA hwaccel (e.g. ProRes) still *accepts* a hw_device_ctx and opens fine, but decodes in
            // software — which would make the session look hardware/zero-copy yet emit CPU frames, freezing the
            // zero-copy convert path. The hw-config probe rules those codecs out up front.
            var preferHardware = optimization == VideoPlaybackOptimization.PlaybackPerformance
                                 && CodecSupportsD3d11va(codec);

            CodecContext codecContext = null!;
            AVBufferRef* hwDeviceCtx = null;
            var usesHardware = false;
            if (preferHardware)
                usesHardware = TryOpenHardware(codec, codecParameters, out codecContext, out hwDeviceCtx);

            if (!usesHardware)
            {
                codecContext = new CodecContext(codec);
                codecContext.FillParameters(codecParameters);
                codecContext.Open();
            }

            // Playback-performance keeps the decoded surface on the GPU (zero-copy, no read-back, no cache); the
            // controller converts it with a compute shader. The GPU converter handles 4:2:0 NV12 (8-bit) and
            // P010/P016 (10/12-bit), adapting the plane-SRV format to the bit depth; if the codec produces some
            // other layout it falls back to hardware read-back here.
            var wantsZeroCopy = usesHardware && optimization == VideoPlaybackOptimization.PlaybackPerformance;
            var zeroCopy = wantsZeroCopy && SupportsZeroCopy(codecContext, codecParameters);
            if (wantsZeroCopy && !zeroCopy)
                Log.Info("Zero-copy decode skipped: stream pixel format isn't a supported 4:2:0 surface. "
                         + "Using hardware decode with CPU read-back.");

            return new VideoDecoderSession(formatContext, codecContext, videoStream, usesHardware, zeroCopy, hwDeviceCtx);
        }
        catch (Exception e)
        {
            error = "Failed to open video: " + e.Message;
            formatContext?.Dispose();
            return null;
        }
        finally
        {
            options?.Dispose();
        }
    }

    /// <summary>
    /// Sets up a D3D11VA hardware decoder on FFmpeg's own D3D11 device (no shared device yet — that's a later
    /// step that has to AddRef the global device). Returns false on any failure so the caller falls back to a
    /// plain software open; never worse than software.
    /// </summary>
    private static unsafe bool TryOpenHardware(Codec codec, CodecParameters codecParameters,
                                               out CodecContext codecContext, out AVBufferRef* hwDeviceCtx)
    {
        codecContext = null!;
        hwDeviceCtx = null;
        try
        {
            // Decode onto TiXL's own D3D11 device (shared) rather than a fresh FFmpeg-owned one: the decoder's
            // output surfaces then live on the same device the compute converter and the output texture use.
            // A separate device would put the surfaces out of reach — using a texture across two D3D11 devices
            // crashes. FFmpeg releases this device on teardown but never AddRefs it, so AddRef once here to keep
            // TiXL's reference alive (a slip here double-frees the global device).
            var deviceCtx = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.D3d11va);
            if (deviceCtx == null)
                return false;
            hwDeviceCtx = deviceCtx;

            EnsureMultithreadProtected();

            var d3d11 = (AVD3D11VADeviceContext*)((AVHWDeviceContext*)deviceCtx->data)->hwctx;
            var devicePtr = ResourceManager.Device.NativePointer;
            Marshal.AddRef(devicePtr);
            d3d11->device = (ID3D11Device*)devicePtr.ToPointer();

            // Serialize FFmpeg's decode against the GPU converter: FFmpeg calls these around its device access,
            // and the converter takes the same lock. Without them the decoder and the converter corrupt each
            // other on the shared device (the decode-fail-then-reinit loop we saw).
            d3d11->@lock = _lockDevice;
            d3d11->unlock = _unlockDevice;

            if (ffmpeg.av_hwdevice_ctx_init(deviceCtx) < 0)
            {
                Marshal.Release(devicePtr);   // init didn't take our AddRef
                d3d11->device = null;          // so the unref below doesn't release TiXL's device
                ffmpeg.av_buffer_unref(&deviceCtx);
                hwDeviceCtx = null;
                return false;
            }

            codecContext = new CodecContext(codec);
            codecContext.FillParameters(codecParameters);

            AVCodecContext* raw = codecContext;
            raw->hw_device_ctx = ffmpeg.av_buffer_ref(deviceCtx);
            raw->get_format = _selectHardwareFormat;

            codecContext.Open();
            return true;
        }
        catch
        {
            codecContext?.Dispose();
            codecContext = null!;
            if (hwDeviceCtx != null)
            {
                var local = hwDeviceCtx;
                ffmpeg.av_buffer_unref(&local);
                hwDeviceCtx = null;
            }

            return false;
        }
    }

    // get_format: pick the D3D11 hardware surface when the decoder offers it, and give the frames context's
    // pool textures the SHADER_RESOURCE bind flag (default D3D11VA pool textures are decode-only) so the GPU
    // converter can wrap them in SRVs. If that setup fails (driver/profile limits) the read-back path still
    // works on the decode-only pool. Falls back to the decoder's first (software) format when D3D11 isn't
    // offered. Held in a static field so the delegate is never collected while FFmpeg holds the function pointer.
    private static unsafe AVPixelFormat SelectHardwareFormat(AVCodecContext* ctx, AVPixelFormat* formats)
    {
        for (var p = formats; *p != AVPixelFormat.None; p++)
        {
            if (*p != AVPixelFormat.D3d11)
                continue;

            // FFmpeg re-calls get_format on every flush — so on every backward seek / scrub. Reuse the existing
            // frames context instead of re-allocating the whole texture pool each time; that churn drops frames.
            if (ctx->hw_frames_ctx != null)
                return AVPixelFormat.D3d11;

            AVBufferRef* framesRef;
            if (ffmpeg.avcodec_get_hw_frames_parameters(ctx, ctx->hw_device_ctx, AVPixelFormat.D3d11, &framesRef) >= 0
                && framesRef != null)
            {
                var framesCtx = (AVHWFramesContext*)framesRef->data;
                var d3d11Frames = (AVD3D11VAFramesContext*)framesCtx->hwctx;
                d3d11Frames->BindFlags |= D3D11BindShaderResource;

                if (ffmpeg.av_hwframe_ctx_init(framesRef) >= 0)
                {
                    ctx->hw_frames_ctx = framesRef;
                    if (!_loggedFramesContext)
                    {
                        Log.Debug("D3D11VA: SHADER_RESOURCE frames context ready (zero-copy capable)");
                        _loggedFramesContext = true;
                    }
                }
                else
                {
                    ffmpeg.av_buffer_unref(&framesRef);
                    Log.Warning("D3D11VA: frames-context init rejected SHADER_RESOURCE; using decode-only read-back");
                }
            }

            return AVPixelFormat.D3d11;
        }

        return *formats;
    }

    // D3D11_BIND_SHADER_RESOURCE — added to the decoder pool's bind flags so the surfaces can be sampled by the
    // GPU NV12→RGBA converter, on top of the decoder's own D3D11_BIND_DECODER.
    private const uint D3D11BindShaderResource = 8;

    private static bool _loggedFramesContext;

    private static readonly unsafe AVCodecContext_get_format _selectHardwareFormat = SelectHardwareFormat;

    // FFmpeg calls these around its decode on the shared device; they take the same lock the GPU converter
    // takes, so decode and convert never overlap. Held in static fields so the delegates aren't collected.
    private static unsafe void LockDevice(void* lockCtx) => Monitor.Enter(HardwareFrameConverter.DeviceLock);
    private static unsafe void UnlockDevice(void* lockCtx) => Monitor.Exit(HardwareFrameConverter.DeviceLock);
    private static readonly unsafe AVD3D11VADeviceContext_lock _lockDevice = LockDevice;
    private static readonly unsafe AVD3D11VADeviceContext_unlock _unlockDevice = UnlockDevice;

    private static bool _multithreadProtected;

    private static void EnsureMultithreadProtected()
    {
        if (_multithreadProtected)
            return;

        using var mt = ResourceManager.Device.QueryInterface<SharpDX.Direct3D11.Multithread>();
        mt.SetMultithreadProtected(true);
        _multithreadProtected = true;
    }

    // The CPU/GPU pixel format a hardware decode will produce. Prefer the codec's reported software format, but
    // libavcodec only sets it on the first get_format (first decode), so before any frame is decoded fall back to
    // the container's bitstream format and map by bit depth: 8-bit 4:2:0 -> NV12, deeper -> P010. Used to label
    // the read-back format for the converter/cache (the GPU converter reads the real surface format directly).
    private static AVPixelFormat HardwareSurfaceFormat(AVPixelFormat swPixelFormat, AVPixelFormat bitstreamFormat)
    {
        if (swPixelFormat != AVPixelFormat.None)
            return swPixelFormat;

        // 8-bit 4:2:0 is 12 bits/pixel; 10-bit is 15 (planar) or 24 (P010), 12-bit higher still.
        return BitsPerPixel(bitstreamFormat) > 12 ? AVPixelFormat.P010le : AVPixelFormat.Nv12;
    }

    // The zero-copy GPU converter handles 4:2:0 NV12/P010/P016 (8/10/12-bit); the plane-SRV format adapts to the
    // bit depth. Other layouts (4:2:2, 4:4:4) stay on read-back. This only runs once hardware decode engaged,
    // which D3D11VA does only for 4:2:0, so the bit-depth bounds are a guard rather than a real filter.
    private static bool SupportsZeroCopy(CodecContext codecContext, CodecParameters codecParameters)
    {
        var format = codecContext.SwPixelFormat != AVPixelFormat.None
                         ? codecContext.SwPixelFormat
                         : (AVPixelFormat)codecParameters.Format;
        var bitsPerPixel = BitsPerPixel(format);
        return bitsPerPixel is >= 12 and <= 24;   // 4:2:0: 8-bit = 12 bpp, up to 12-bit P016 = 24 bpp
    }

    private static unsafe int BitsPerPixel(AVPixelFormat format)
    {
        var desc = ffmpeg.av_pix_fmt_desc_get(format);
        return desc != null ? ffmpeg.av_get_bits_per_pixel(desc) : 0;
    }

    // True only when the decoder advertises a D3D11VA device config — i.e. it can genuinely decode this codec on
    // the GPU. Decoders without one (ProRes, FFV1, …) accept a hw_device_ctx but fall back to software silently.
    private static unsafe bool CodecSupportsD3d11va(Codec codec)
    {
        AVCodec* c = codec;
        if (c == null)
            return false;

        for (var i = 0; ; i++)
        {
            var config = ffmpeg.avcodec_get_hw_config(c, i);
            if (config == null)
                return false;

            if (config->device_type == AVHWDeviceType.D3d11va
                && (config->methods & AvCodecHwConfigMethodHwDeviceCtx) != 0)
                return true;
        }
    }

    // AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX — the decoder is driven via a hw_device_ctx (our D3D11VA path).
    private const int AvCodecHwConfigMethodHwDeviceCtx = 0x01;

    /// <summary>
    /// Decodes the next frame in presentation order into <see cref="CurrentFrame"/>. Returns false at
    /// end-of-stream. This is the fast path: forward playback and export stay here and never seek. In hardware
    /// mode a GPU surface is transferred to CPU memory before returning.
    /// </summary>
    public unsafe bool TryReadNextFrame(out long framePts)
    {
        framePts = 0;
        while (true)
        {
            var receive = _codecContext.ReceiveFrame(_frame);
            if (receive == CodecResult.Success)
            {
                var pts = _frame.BestEffortTimestamp;
                framePts = pts != NoPts ? pts : _frame.Pts;

                // Read the GPU surface back to CPU memory only when not zero-copy; otherwise leave it on the GPU
                // for the compute-shader converter.
                _lastFrameReadBack = (AVPixelFormat)_frame.Format == AVPixelFormat.D3d11 && !_zeroCopy;
                if (_lastFrameReadBack)
                {
                    _swFrame.Unref();
                    if (ffmpeg.av_hwframe_transfer_data(_swFrame, _frame, 0) < 0)
                        return false;
                }

                return true;
            }

            if (receive == CodecResult.EOF)
                return false;

            // CodecResult.Again — the decoder needs another packet.
            if (_draining)
                return false;

            var read = _formatContext.ReadFrame(_packet);
            if (read == CodecResult.EOF)
            {
                // Flush the decoder so it emits any buffered frames, then drain on the next receive.
                _draining = true;
                SendDrainPacket();
                continue;
            }

            try
            {
                if (_packet.StreamIndex == _videoStreamIndex)
                    _codecContext.SendPacket(_packet);
            }
            finally
            {
                _packet.Unref();
            }
        }
    }

    /// <summary>
    /// Seeks to the keyframe at or before <paramref name="targetPts"/> and flushes the decoder. The caller
    /// then decodes forward (<see cref="TryReadNextFrame"/>) until reaching the target frame.
    /// </summary>
    public void SeekToKeyframeBefore(long targetPts)
    {
        _formatContext.SeekFrame(targetPts, _videoStreamIndex, AVSEEK_FLAG.Backward);
        FlushDecoder();
        _draining = false;
    }

    /// <summary>
    /// Exact seek: keyframe seek then decode-forward to the first frame whose PTS reaches
    /// <paramref name="targetPts"/>. <paramref name="targetPts"/> is expected to already sit on the frame
    /// grid (the controller floors seconds→PTS), so this lands on the intended frame. Returns false if the
    /// stream ends before the target (target past end).
    /// </summary>
    public bool SeekAndDecodeTo(long targetPts, out long framePts)
    {
        SeekToKeyframeBefore(targetPts);
        framePts = 0;
        while (TryReadNextFrame(out var pts))
        {
            framePts = pts;
            if (pts >= targetPts)
                return true;
        }

        return false;
    }

    public unsafe void Dispose()
    {
        _frame.Dispose();
        _swFrame.Dispose();
        _packet.Dispose();
        _codecContext.Dispose();
        _formatContext.Dispose();

        if (_hwDeviceCtx != null)
        {
            var local = _hwDeviceCtx;
            ffmpeg.av_buffer_unref(&local);
            _hwDeviceCtx = null;
        }
    }

    private unsafe VideoDecoderSession(FormatContext formatContext, CodecContext codecContext,
                                       MediaStream videoStream, bool usesHardware, bool zeroCopy, AVBufferRef* hwDeviceCtx)
    {
        _formatContext = formatContext;
        _codecContext = codecContext;
        _videoStreamIndex = videoStream.Index;
        _hwDeviceCtx = hwDeviceCtx;
        UsesHardwareDecode = usesHardware;
        UsesZeroCopy = zeroCopy;
        _zeroCopy = zeroCopy;

        var timeBase = videoStream.TimeBase;
        TimeBaseNum = timeBase.Num;
        TimeBaseDen = timeBase.Den;
        StreamStartPts = videoStream.StartTime != NoPts ? videoStream.StartTime : 0;

        Width = codecContext.Width;
        Height = codecContext.Height;

        // In hardware mode the codec's pix_fmt is D3d11; the CPU read-back is the codec's sw_pix_fmt (Nv12 for
        // 8-bit, P010 for 10-bit). sw_pix_fmt isn't reported until the first decode, so derive it from the
        // bitstream bit depth meanwhile (so the format label, HDR test, and cache budget are right from frame 0).
        var cpuFormat = codecContext.PixelFormat;
        if (usesHardware)
            cpuFormat = HardwareSurfaceFormat(codecContext.SwPixelFormat, (AVPixelFormat)(videoStream.Codecpar?.Format ?? -1));
        PixelFormat = cpuFormat;
        IsHdr = DetectHdr(codecContext, cpuFormat);

        var avg = videoStream.AvgFrameRate;
        FrameRate = avg.Den != 0 ? avg.Num / (double)avg.Den : 0;
        DurationSeconds = ComputeDurationSeconds(formatContext, videoStream, timeBase);
    }

    private static double ComputeDurationSeconds(FormatContext formatContext, MediaStream videoStream, AVRational timeBase)
    {
        if (videoStream.Duration != NoPts && timeBase.Den != 0)
            return videoStream.Duration * timeBase.Num / (double)timeBase.Den;

        // FormatContext.Duration is in AV_TIME_BASE (microsecond) units.
        if (formatContext.Duration > 0)
            return formatContext.Duration / (double)ffmpeg.AV_TIME_BASE;

        return 0;
    }

    private static bool DetectHdr(CodecContext codecContext, AVPixelFormat cpuFormat)
    {
        if (codecContext.ColorTrc is AVColorTransferCharacteristic.Smpte2084 or AVColorTransferCharacteristic.AribStdB67)
            return true;

        return cpuFormat is AVPixelFormat.P010le or AVPixelFormat.P010be
                         or AVPixelFormat.P016le or AVPixelFormat.P016be;
    }

    private unsafe void FlushDecoder() => ffmpeg.avcodec_flush_buffers(_codecContext);

    // A null packet puts the decoder into drain mode so it emits its remaining buffered frames.
    private unsafe void SendDrainPacket() => ffmpeg.avcodec_send_packet(_codecContext, null);

    private static readonly long NoPts = ffmpeg.AV_NOPTS_VALUE;

    private readonly FormatContext _formatContext;
    private readonly CodecContext _codecContext;
    private readonly int _videoStreamIndex;
    private readonly Packet _packet = new();
    private readonly Frame _frame = new();
    private readonly Frame _swFrame = new();
    private unsafe AVBufferRef* _hwDeviceCtx;
    private readonly bool _zeroCopy;
    private bool _lastFrameReadBack;
    private bool _draining;
}

using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;

namespace T3.Video;

/// <summary>
/// Wraps one open video file: its demuxer (<see cref="FormatContext"/>) and decoder
/// (<see cref="CodecContext"/>). Provides the two access patterns the playback controller needs —
/// <see cref="TryReadNextFrame"/> for the fast sequential stream (forward play / export) and
/// <see cref="SeekToKeyframeBefore"/> + decode-forward for exact, frame-accurate seeking.
///
/// Not thread-safe: a session is owned by exactly one decode worker thread. FFmpeg's contexts are not
/// reentrant, so single ownership avoids locking them.
/// </summary>
public sealed class VideoDecoderSession : IDisposable
{
    public double DurationSeconds { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>The decoder's output pixel format (e.g. <c>Nv12</c>, <c>Yuv420p</c>, <c>P010le</c>).</summary>
    public AVPixelFormat PixelFormat { get; }

    /// <summary>True for PQ/HLG transfer or ≥10-bit formats — the converter should target RGBA16.</summary>
    public bool IsHdr { get; }

    public int TimeBaseNum { get; }
    public int TimeBaseDen { get; }

    /// <summary>The stream's <c>start_time</c> in time-base ticks (0 when unset).</summary>
    public long StreamStartPts { get; }

    /// <summary>Nominal average frame rate (fps); 0 when unknown.</summary>
    public double FrameRate { get; }

    /// <summary>
    /// The most recently decoded frame; valid after <see cref="TryReadNextFrame"/> returned true, until the
    /// next read. Backends read its planes (<c>Data</c>/<c>Linesize</c>) before advancing.
    /// </summary>
    public Frame CurrentFrame => _frame;

    /// <summary>Opens <paramref name="url"/> and selects the best video stream. Returns null on failure.</summary>
    public static VideoDecoderSession? TryOpen(string url, out string? error)
    {
        error = null;
        if (!FfmpegLibrary.EnsureInitialized())
        {
            error = FfmpegLibrary.StatusError ?? "FFmpeg is not available";
            return null;
        }

        FormatContext? formatContext = null;
        try
        {
            formatContext = FormatContext.OpenInputUrl(url);
            formatContext.LoadStreamInfo();

            var stream = formatContext.FindBestStreamOrNull(AVMediaType.Video);
            if (stream == null)
            {
                error = "No video stream found in " + url;
                formatContext.Dispose();
                return null;
            }

            var videoStream = stream.Value;
            var codec = Codec.FindDecoderById(videoStream.Codecpar.CodecId);
            var codecContext = new CodecContext(codec);
            codecContext.FillParameters(videoStream.Codecpar);
            codecContext.Open();

            return new VideoDecoderSession(formatContext, codecContext, videoStream);
        }
        catch (Exception e)
        {
            error = "Failed to open video: " + e.Message;
            formatContext?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Decodes the next frame in presentation order into <see cref="CurrentFrame"/>. Returns false at
    /// end-of-stream. This is the fast path: forward playback and export stay here and never seek.
    /// </summary>
    public bool TryReadNextFrame(out long framePts)
    {
        framePts = 0;
        while (true)
        {
            var receive = _codecContext.ReceiveFrame(_frame);
            if (receive == CodecResult.Success)
            {
                var pts = _frame.BestEffortTimestamp;
                framePts = pts != NoPts ? pts : _frame.Pts;
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

    public void Dispose()
    {
        _frame.Dispose();
        _packet.Dispose();
        _codecContext.Dispose();
        _formatContext.Dispose();
    }

    private VideoDecoderSession(FormatContext formatContext, CodecContext codecContext, MediaStream videoStream)
    {
        _formatContext = formatContext;
        _codecContext = codecContext;
        _videoStreamIndex = videoStream.Index;

        var timeBase = videoStream.TimeBase;
        TimeBaseNum = timeBase.Num;
        TimeBaseDen = timeBase.Den;
        StreamStartPts = videoStream.StartTime != NoPts ? videoStream.StartTime : 0;

        Width = codecContext.Width;
        Height = codecContext.Height;
        PixelFormat = codecContext.PixelFormat;
        IsHdr = DetectHdr(codecContext);

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

    private static bool DetectHdr(CodecContext codecContext)
    {
        if (codecContext.ColorTrc is AVColorTransferCharacteristic.Smpte2084 or AVColorTransferCharacteristic.AribStdB67)
            return true;

        return codecContext.PixelFormat is AVPixelFormat.P010le or AVPixelFormat.P010be
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
    private bool _draining;
}

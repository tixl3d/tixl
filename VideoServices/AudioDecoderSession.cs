using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;
using T3.Core.Logging;

namespace T3.VideoServices;

/// <summary>
/// Decodes one media file's audio track into interleaved stereo float PCM at the mixer's sample rate.
/// Owns its own demuxer, decoder and resampler — deliberately <b>not</b> shared with
/// <see cref="VideoDecoderSession"/>: the video worker skips demuxing whenever it serves a frame from its
/// cache, and a preview proxy carries no audio track at all, so audio riding along on the video pass would
/// starve exactly when playback is smooth and go silent whenever a proxy is substituted. Audio therefore
/// always reads the original file and seeks on its own.
///
/// Not thread-safe: a session is owned by exactly one worker thread (FFmpeg's contexts are not reentrant).
/// </summary>
public sealed class AudioDecoderSession : IDisposable
{
    /// <summary>Decoded PCM is always downmixed/upmixed to stereo — what the BASS mixers expect.</summary>
    public const int OutputChannels = 2;

    public int OutputSampleRate { get; }

    /// <summary>The track's own rate and channel count, before resampling. Diagnostics only.</summary>
    public int SourceSampleRate { get; }

    public int SourceChannels { get; }

    /// <summary>Length of the audio stream in seconds; 0 when the container doesn't report one.</summary>
    public double DurationSeconds { get; }

    /// <summary>
    /// Opens the best audio stream of <paramref name="url"/> and prepares a resampler to interleaved float /
    /// <paramref name="outputSampleRate"/> / stereo. Returns null both when the file has no audio track
    /// (<paramref name="error"/> stays null — a normal case) and on failure (<paramref name="error"/> set).
    /// <paramref name="demuxerOptions"/> passes demuxer options (e.g. <c>rtsp_transport=tcp</c>).
    /// </summary>
    public static unsafe AudioDecoderSession? TryOpen(string url, int outputSampleRate, out string? error,
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
        CodecContext? codecContext = null;
        SwrContext* swr = null;
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

            var stream = formatContext.FindBestStreamOrNull(AVMediaType.Audio);
            if (stream == null)
            {
                formatContext.Dispose();
                return null;
            }

            var audioStream = stream.Value;
            var codecParameters = audioStream.Codecpar;
            if (codecParameters == null)
            {
                error = "Audio stream has no codec parameters: " + url;
                formatContext.Dispose();
                return null;
            }

            codecContext = new CodecContext(Codec.FindDecoderById(codecParameters.CodecId));
            codecContext.FillParameters(codecParameters);
            codecContext.Open();

            AVCodecContext* rawCodec = codecContext;
            if (rawCodec->sample_rate <= 0)
            {
                error = "Audio stream has no sample rate: " + url;
                codecContext.Dispose();
                formatContext.Dispose();
                return null;
            }

            AVChannelLayout outputLayout = default;
            ffmpeg.av_channel_layout_default(&outputLayout, OutputChannels);

            var allocated = ffmpeg.swr_alloc_set_opts2(&swr,
                                                       &outputLayout, AVSampleFormat.Flt, outputSampleRate,
                                                       &rawCodec->ch_layout, rawCodec->sample_fmt, rawCodec->sample_rate,
                                                       0, null);
            if (allocated < 0 || swr == null || ffmpeg.swr_init(swr) < 0)
            {
                error = "Could not initialize the audio resampler for " + url;
                if (swr != null)
                    ffmpeg.swr_free(&swr);
                codecContext.Dispose();
                formatContext.Dispose();
                return null;
            }

            return new AudioDecoderSession(url, formatContext, codecContext, audioStream, swr, outputSampleRate);
        }
        catch (Exception e)
        {
            error = "Failed to open audio: " + e.Message;
            if (swr != null)
                ffmpeg.swr_free(&swr);
            codecContext?.Dispose();
            formatContext?.Dispose();
            return null;
        }
        finally
        {
            options?.Dispose();
        }
    }

    /// <summary>
    /// Seeks to the packet at or before <paramref name="seconds"/> and drops everything buffered in the
    /// decoder and the resampler, so the next <see cref="TryDecodeChunk"/> starts a fresh, continuous run.
    /// The landing point is the preceding keyframe/packet boundary, so it can be slightly early — the feeder
    /// treats the reported chunk start as the truth rather than assuming the request was hit exactly.
    /// </summary>
    public unsafe void SeekTo(double seconds)
    {
        var targetPts = TimeToFrameMapper.SecondsToPts(Math.Max(0, seconds), _streamStartPts, _timeBaseNum, _timeBaseDen);
        _formatContext.SeekFrame(targetPts, _audioStreamIndex, AVSEEK_FLAG.Backward);
        ffmpeg.avcodec_flush_buffers(_codecContext);

        // Re-initializing drops the resampler's internal history, so the run after a seek carries no samples
        // from before it.
        ffmpeg.swr_init(_swr);

        _draining = false;
        _needsAnchor = true;
    }

    /// <summary>
    /// Decodes the next chunk of PCM. <paramref name="samples"/> is interleaved stereo float, valid until the
    /// next call (the buffer is reused). <paramref name="startSeconds"/> is the chunk's position in source
    /// time, counted forward from the first frame decoded after the last seek, so consecutive chunks are
    /// exactly contiguous. Returns false at end of stream.
    /// </summary>
    public unsafe bool TryDecodeChunk(out ReadOnlySpan<float> samples, out double startSeconds)
    {
        samples = default;
        startSeconds = 0;

        while (true)
        {
            var receive = _codecContext.ReceiveFrame(_frame);
            if (receive == CodecResult.Success)
            {
                var converted = Resample(out var floatCount);
                if (converted == 0)
                    continue; // the resampler is still filling its window — no output yet

                if (_needsAnchor)
                {
                    _outputSeconds = FrameSeconds();
                    _needsAnchor = false;
                }

                samples = _pcm.AsSpan(0, floatCount);
                startSeconds = _outputSeconds;
                _outputSeconds += converted / (double)OutputSampleRate;
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
                _draining = true;
                ffmpeg.avcodec_send_packet(_codecContext, null); // drain what the decoder still holds
                continue;
            }

            try
            {
                if (_packet.StreamIndex == _audioStreamIndex)
                    _codecContext.SendPacket(_packet);
            }
            finally
            {
                _packet.Unref();
            }
        }
    }

    public unsafe void Dispose()
    {
        if (_swr != null)
        {
            var local = _swr;
            ffmpeg.swr_free(&local);
            _swr = null;
        }

        _frame.Dispose();
        _packet.Dispose();
        _codecContext.Dispose();
        _formatContext.Dispose();
    }

    // Converts the decoded frame into the interleaved output buffer. Returns samples per channel.
    private unsafe int Resample(out int floatCount)
    {
        AVFrame* raw = _frame;
        var inputRate = _inputSampleRate;

        // Room for what this frame produces plus whatever the resampler still holds back.
        var pending = ffmpeg.swr_get_delay(_swr, inputRate);
        var maxSamples = (int)ffmpeg.av_rescale_rnd(pending + raw->nb_samples, OutputSampleRate, inputRate, AVRounding.Up);
        var needed = maxSamples * OutputChannels;
        if (_pcm.Length < needed)
            _pcm = new float[needed];

        int converted;
        fixed (float* destination = _pcm)
        {
            var destinationBytes = (byte*)destination;
            converted = ffmpeg.swr_convert(_swr, &destinationBytes, maxSamples, raw->extended_data, raw->nb_samples);
        }

        if (converted < 0)
        {
            Log.Warning($"Audio resampling failed for '{_url}' (code {converted})");
            converted = 0;
        }

        floatCount = converted * OutputChannels;
        return converted;
    }

    private unsafe double FrameSeconds()
    {
        AVFrame* raw = _frame;
        var pts = raw->best_effort_timestamp != NoPts ? raw->best_effort_timestamp : raw->pts;
        return pts != NoPts ? TimeToFrameMapper.PtsToSeconds(pts, _streamStartPts, _timeBaseNum, _timeBaseDen) : 0;
    }

    private unsafe AudioDecoderSession(string url, FormatContext formatContext, CodecContext codecContext,
                                       MediaStream audioStream, SwrContext* swr, int outputSampleRate)
    {
        _url = url;
        _formatContext = formatContext;
        _codecContext = codecContext;
        _audioStreamIndex = audioStream.Index;
        _swr = swr;
        OutputSampleRate = outputSampleRate;

        AVCodecContext* rawCodec = codecContext;
        _inputSampleRate = rawCodec->sample_rate;
        SourceSampleRate = rawCodec->sample_rate;
        SourceChannels = rawCodec->ch_layout.nb_channels;

        var timeBase = audioStream.TimeBase;
        _timeBaseNum = timeBase.Num;
        _timeBaseDen = timeBase.Den;
        _streamStartPts = audioStream.StartTime != NoPts ? audioStream.StartTime : 0;

        DurationSeconds = ComputeDurationSeconds(formatContext, audioStream, timeBase);
    }

    // Some containers (notably Matroska) report duration only on the container, not per stream. Without the
    // fallback the feeder resolves every request to 0 and the track never plays.
    private static double ComputeDurationSeconds(FormatContext formatContext, MediaStream audioStream, AVRational timeBase)
    {
        if (audioStream.Duration != NoPts && timeBase.Den != 0)
            return audioStream.Duration * timeBase.Num / (double)timeBase.Den;

        // FormatContext.Duration is in AV_TIME_BASE (microsecond) units.
        if (formatContext.Duration > 0)
            return formatContext.Duration / (double)ffmpeg.AV_TIME_BASE;

        return 0;
    }

    private static readonly long NoPts = ffmpeg.AV_NOPTS_VALUE;

    private readonly FormatContext _formatContext;
    private readonly CodecContext _codecContext;
    private readonly int _audioStreamIndex;
    private readonly int _inputSampleRate;
    private readonly int _timeBaseNum;
    private readonly int _timeBaseDen;
    private readonly long _streamStartPts;
    private readonly string _url;
    private readonly Packet _packet = new();
    private readonly Frame _frame = new();
    private unsafe SwrContext* _swr;
    private float[] _pcm = [];
    private double _outputSeconds;
    private bool _needsAnchor = true;
    private bool _draining;
}

#nullable enable
using System;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;

namespace T3.VideoServices;

/// <summary>
/// Decides whether a video source actually benefits from a seek-proxy. A proxy only helps <b>long-GOP</b>
/// inter-frame video (H.264/HEVC/VP9/…), where a random seek must decode from the previous keyframe; all-intra
/// sources (ProRes / DNxHR / MJPEG / HAP) and short-GOP files already seek cheaply and are skipped.
///
/// The test is <b>codec-agnostic</b>: it measures the actual keyframe spacing from the bitstream, so an
/// I-frame-only H.264 is correctly reported "not needed" while a long-GOP HEVC is "recommended". Demux-only —
/// it reads packet headers, never decodes a frame — so it's cheap and runs off the editor's enqueue path.
/// </summary>
public static class ProxyEligibility
{
    public enum Recommendation
    {
        /// <summary>Already seek-cheap (all-intra or short-GOP) — generating a proxy would waste disk and time.</summary>
        NotNeeded,

        /// <summary>Long-GOP inter-frame video — a proxy makes scrubbing/seeking cheap.</summary>
        Recommended,

        /// <summary>Couldn't tell (file unreadable, no video stream, no usable timestamps).</summary>
        Unknown,
    }

    /// <summary>Keyframes more than this far apart make a clip worth proxying: a random seek has to decode up to
    /// this many seconds of frames, which a proxy eliminates. The line sits well above all-intra (one keyframe per
    /// frame ≈ 1/fps ≈ 0.03 s) so ProRes/HAP/MJPEG are skipped, yet below the ~1 s GOP common to camera and
    /// screen-recording H.264/HEVC so those are caught. Tunable — could later scale with resolution.</summary>
    public const double LongGopSeconds = 0.75;

    public readonly record struct Result(Recommendation Recommendation, double KeyframeIntervalSeconds, string Reason);

    /// <summary>Probes <paramref name="sourcePath"/> and reports whether a seek-proxy is worth generating.
    /// Never throws — failures map to <see cref="Recommendation.Unknown"/>.</summary>
    public static Result Evaluate(string sourcePath)
    {
        if (!FfmpegLibrary.EnsureInitialized())
            return new Result(Recommendation.Unknown, 0, FfmpegLibrary.StatusError ?? "FFmpeg is not available");

        FormatContext? formatContext = null;
        try
        {
            formatContext = FormatContext.OpenInputUrl(sourcePath, null, null);
            formatContext.LoadStreamInfo();

            var stream = formatContext.FindBestStreamOrNull(AVMediaType.Video);
            if (stream == null)
                return new Result(Recommendation.Unknown, 0, "No video stream found");

            var videoStream = stream.Value;
            var timeBase = videoStream.TimeBase;
            var secondsPerTick = timeBase.Den != 0 ? timeBase.Num / (double)timeBase.Den : 0;
            if (secondsPerTick <= 0)
                return new Result(Recommendation.Unknown, 0, "Video stream has no usable time base");

            var codecName = CodecName(videoStream);
            var (interval, capped, found) = MeasureKeyframeInterval(formatContext, videoStream.Index, secondsPerTick);

            if (!found)
                return new Result(Recommendation.Unknown, 0, $"Could not measure keyframe spacing of {codecName}");

            // Capped before EOF with a single keyframe ⇒ the GOP is longer than the whole sampled span: clearly long.
            if (capped || interval > LongGopSeconds)
                return new Result(Recommendation.Recommended, interval,
                                  $"Long-GOP {codecName} (~{interval:0.0}s between keyframes) — a proxy speeds up seeking");

            return new Result(Recommendation.NotNeeded, interval,
                              $"{codecName} already seeks cheaply (~{interval:0.0}s between keyframes) — no proxy needed");
        }
        catch (Exception e)
        {
            return new Result(Recommendation.Unknown, 0, "Could not read video: " + e.Message);
        }
        finally
        {
            formatContext?.Dispose();
        }
    }

    // Reads packet headers until it finds two keyframes (the gap = GOP length) or hits the sample cap. Returns the
    // interval in seconds, whether it stopped at the cap (GOP longer than the sample), and whether anything usable
    // was found. A single keyframe at EOF means a short single-GOP file — its sampled span is the interval.
    private static (double interval, bool capped, bool found) MeasureKeyframeInterval(
        FormatContext formatContext, int streamIndex, double secondsPerTick)
    {
        var packet = new Packet();
        var firstKeyTs = NoPts;
        var lastTs = NoPts;
        var secondKeyTs = NoPts;
        var videoPacketsSeen = 0;
        var reachedEof = false;
        try
        {
            while (videoPacketsSeen < MaxSampledPackets)
            {
                if (formatContext.ReadFrame(packet) == CodecResult.EOF)
                {
                    reachedEof = true;
                    break;
                }

                try
                {
                    if (packet.StreamIndex != streamIndex)
                        continue;

                    videoPacketsSeen++;
                    var ts = packet.Dts != NoPts ? packet.Dts : packet.Pts;
                    if (ts != NoPts)
                        lastTs = ts;

                    if (((int)packet.Flags & ffmpeg.AV_PKT_FLAG_KEY) == 0 || ts == NoPts)
                        continue;

                    if (firstKeyTs == NoPts)
                    {
                        firstKeyTs = ts;
                    }
                    else
                    {
                        secondKeyTs = ts;
                        break;
                    }
                }
                finally
                {
                    packet.Unref();
                }
            }
        }
        finally
        {
            packet.Dispose();
        }

        if (firstKeyTs == NoPts)
            return (0, false, false);

        if (secondKeyTs != NoPts)
            return (Math.Abs(secondKeyTs - firstKeyTs) * secondsPerTick, false, true);

        // Only one keyframe in the sample: long-GOP if we stopped at the cap, otherwise a short single-GOP file
        // whose whole span is one GOP.
        var span = lastTs != NoPts ? Math.Abs(lastTs - firstKeyTs) * secondsPerTick : 0;
        return (span, capped: !reachedEof, found: true);
    }

    private static string CodecName(MediaStream videoStream)
    {
        var id = videoStream.Codecpar?.CodecId ?? AVCodecID.None;
        try
        {
            var name = Codec.FindDecoderById(id).Name;
            return string.IsNullOrEmpty(name) ? id.ToString() : name;
        }
        catch
        {
            return id.ToString();
        }
    }

    // ~40 s of 30 fps content: far beyond any sane GOP, so failing to find a second keyframe within it means the
    // stream really is one very long GOP.
    private const int MaxSampledPackets = 1200;

    private static readonly long NoPts = ffmpeg.AV_NOPTS_VALUE;
}

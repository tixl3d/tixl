#nullable enable
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;

namespace T3.VideoServices;

/// <summary>
/// Demux-only probe of a video container's metadata — it opens the file and reads stream headers, never
/// decoding a frame, so it's cheap enough to run on a user-triggered action. Reached from the editor through
/// <see cref="T3.Core.Video.IVideoEncoderFactory"/>.
/// </summary>
internal static class VideoMetadata
{
    /// <summary>Reads <paramref name="sourcePath"/>'s duration in seconds. Returns false (and 0) when the file
    /// can't be opened or has no usable duration. Never throws.</summary>
    public static bool TryProbeDurationSeconds(string sourcePath, out double seconds)
    {
        seconds = 0;
        if (!FfmpegLibrary.EnsureInitialized())
            return false;

        FormatContext? formatContext = null;
        try
        {
            formatContext = FormatContext.OpenInputUrl(sourcePath, null, null);
            formatContext.LoadStreamInfo();

            // Prefer the video stream's own duration; fall back to the container duration (microsecond units)
            // for files whose stream carries no duration timestamp.
            var stream = formatContext.FindBestStreamOrNull(AVMediaType.Video);
            if (stream != null)
            {
                var videoStream = stream.Value;
                var timeBase = videoStream.TimeBase;
                if (videoStream.Duration != ffmpeg.AV_NOPTS_VALUE && timeBase.Den != 0)
                {
                    seconds = videoStream.Duration * timeBase.Num / (double)timeBase.Den;
                    if (seconds > 0)
                        return true;
                }
            }

            if (formatContext.Duration > 0)
            {
                seconds = formatContext.Duration / (double)ffmpeg.AV_TIME_BASE;
                return seconds > 0;
            }

            return false;
        }
        catch
        {
            seconds = 0;
            return false;
        }
        finally
        {
            formatContext?.Dispose();
        }
    }
}

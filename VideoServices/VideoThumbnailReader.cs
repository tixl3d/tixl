using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;
using T3.Core.Video;

namespace T3.VideoServices;

/// <summary>
/// The FFmpeg implementation of <see cref="IVideoThumbnailReader"/>: one private software decoder session
/// (fast-seeking mode) plus a swscale pass that scales the decoded frame straight down to thumbnail size.
/// Deliberately separate from <see cref="VideoPlaybackController"/> and its <see cref="VideoFrameCache"/> so
/// thumbnail grabs never evict playback frames. Owned by one background thread.
/// </summary>
internal sealed class VideoThumbnailReader : IVideoThumbnailReader
{
    public double DurationSeconds => _session.DurationSeconds;

    public static VideoThumbnailReader? TryOpen(string sourcePath, out string? error)
    {
        var session = VideoDecoderSession.TryOpen(sourcePath, VideoPlaybackOptimization.FastSeeking, out error);
        return session == null ? null : new VideoThumbnailReader(session);
    }

    public bool TryReadFrame(double seconds, int targetWidth, int targetHeight, byte[] rgbaBuffer)
    {
        if (targetWidth <= 0 || targetHeight <= 0 || rgbaBuffer.Length < targetWidth * targetHeight * 4)
            return false;

        try
        {
            // Back off slightly from the nominal end so a request at the exact duration still lands on the
            // last decodable frame instead of running the decoder dry.
            var duration = _session.DurationSeconds;
            var clamped = duration > 0
                              ? Math.Clamp(seconds, 0, Math.Max(0, duration - 0.05))
                              : Math.Max(0, seconds);

            var targetPts = TimeToFrameMapper.SecondsToFramePts(clamped, _session.StreamStartPts,
                                                                _session.TimeBaseNum, _session.TimeBaseDen,
                                                                _session.FrameRate);
            if (!_session.SeekAndDecodeTo(targetPts, out _))
                return false;

            var source = _session.CurrentFrame;
            if (source.Width <= 0 || source.Height <= 0)
                return false;

            // Aspect-fit into the target rect; swscale wants even dimensions.
            var scale = Math.Min(targetWidth / (double)source.Width, targetHeight / (double)source.Height);
            var fitWidth = Math.Max(2, (int)(source.Width * scale) & ~1);
            var fitHeight = Math.Max(2, (int)(source.Height * scale) & ~1);

            if (_scaled == null || _scaled.Width != fitWidth || _scaled.Height != fitHeight)
            {
                _scaled?.Dispose();
                _scaled = Frame.CreateVideo(fitWidth, fitHeight, AVPixelFormat.Rgba);
                _scaled.EnsureBuffer(1);
            }

            _converter.ConvertFrame(source, _scaled, SWS.Bilinear);

            Array.Clear(rgbaBuffer, 0, targetWidth * targetHeight * 4);
            var offsetX = (targetWidth - fitWidth) / 2;
            var offsetY = (targetHeight - fitHeight) / 2;
            var sourceStride = _scaled.Linesize[0];
            var sourceData = _scaled.Data[0];
            for (var y = 0; y < fitHeight; y++)
            {
                var destOffset = ((offsetY + y) * targetWidth + offsetX) * 4;
                System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(sourceData, y * sourceStride),
                                                            rgbaBuffer, destOffset, fitWidth * 4);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _scaled?.Dispose();
        _converter.Free();
        _session.Dispose();
    }

    private VideoThumbnailReader(VideoDecoderSession session) => _session = session;

    private readonly VideoDecoderSession _session;
    private readonly VideoFrameConverter _converter = new();
    private Frame? _scaled;
}

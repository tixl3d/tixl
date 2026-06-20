#nullable enable
using System;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.DataTypes.Vector;
using T3.Core.Video;

namespace T3.Editor.Gui.Windows.RenderExport;

/// <summary>
/// Rough pre-export estimates of output file size and render time for the render-to-file UI. Both are
/// deliberately approximate ("guesstimates"): size depends on content; render time depends on graph complexity
/// (we proxy it with the editor's recent wall-clock frame time), motion-blur samples, codec speed, and the
/// machine. Good enough to flag "this will be huge / slow" before committing.
/// </summary>
internal static class RenderExportEstimate
{
    /// <summary>Estimated output size in bytes (0 when not estimable).</summary>
    public static long EstimateBytes(VideoExportCodec codec, Int2 resolution, int frameCount, double durationSec, long bitRate)
    {
        if (frameCount <= 0)
            return 0;

        var (w, h) = codec.RoundToEncoderBlock(resolution.Width, resolution.Height);
        var pixels = (double)w * h;

        return codec switch
                   {
                       // HAP is fixed-ratio DXT (Snappy shaves a little).
                       VideoExportCodec.Hap => (long)(0.5 * pixels * frameCount),
                       VideoExportCodec.HapAlpha => (long)(1.0 * pixels * frameCount),
                       VideoExportCodec.HapQ => (long)(1.0 * pixels * frameCount),
                       // Rate-controlled: bitrate × duration.
                       VideoExportCodec.H264 or VideoExportCodec.Hevc or VideoExportCodec.VP9 or VideoExportCodec.AV1
                           => (long)(bitRate / 8.0 * durationSec),
                       // ProRes 422 ≈ ~0.8 bits/pixel·frame; FFV1 lossless ≈ ~half of raw 8-bit RGB. Both rough.
                       VideoExportCodec.ProRes => (long)(0.10 * pixels * frameCount),
                       VideoExportCodec.FFV1 => (long)(0.5 * pixels * 3 * frameCount),
                       _ => 0,
                   };
    }

    /// <summary>Estimated total render time in seconds: per-frame graph render (recent editor frame time × motion
    /// blur samples) plus a rough per-codec encode cost, across all frames.</summary>
    public static double EstimateSeconds(VideoExportCodec codec, Int2 resolution, int frameCount, int motionBlurSamples)
    {
        if (frameCount <= 0)
            return 0;

        var samples = motionBlurSamples > 0 ? motionBlurSamples : 1;

        var renderPerFrame = SmoothedRenderPerFrame();

        var megapixels = (double)resolution.Width * resolution.Height / 1e6;
        var encodePerFrame = megapixels / EncodeMegapixelsPerSec(codec);

        return frameCount * (renderPerFrame * samples + encodePerFrame);
    }

    // The editor's recent wall-clock frame time captures the graph's render cost, but it jitters every frame —
    // shown raw it makes the estimate flicker (34→35→33 s). Low-pass it, updated at most once per UI frame so the
    // many per-item calls in one dropdown draw all see the same value. Guard against a stale/zero or absurd reading
    // (e.g. right after a stall) with a 60 fps fallback.
    private static double SmoothedRenderPerFrame()
    {
        var frame = ImGui.GetFrameCount();
        if (frame != _smoothedFrame)
        {
            _smoothedFrame = frame;
            var raw = Playback.LastFrameDuration is > 0 and < 10 ? Playback.LastFrameDuration : 1.0 / 60;
            _smoothedRenderPerFrame = _smoothedRenderPerFrame > 0
                                          ? _smoothedRenderPerFrame * 0.9 + raw * 0.1
                                          : raw;
        }

        return _smoothedRenderPerFrame;
    }

    private static int _smoothedFrame = -1;
    private static double _smoothedRenderPerFrame;

    // Rough encode throughput (megapixels/sec) on a typical desktop; H.264/HEVC assume the hardware path.
    private static double EncodeMegapixelsPerSec(VideoExportCodec codec) => codec switch
                                                                                {
                                                                                    VideoExportCodec.H264 => 300,
                                                                                    VideoExportCodec.Hevc => 200,
                                                                                    VideoExportCodec.Hap => 150,
                                                                                    VideoExportCodec.HapAlpha => 150,
                                                                                    VideoExportCodec.HapQ => 150,
                                                                                    VideoExportCodec.ProRes => 120,
                                                                                    VideoExportCodec.FFV1 => 80,
                                                                                    VideoExportCodec.AV1 => 40,
                                                                                    VideoExportCodec.VP9 => 25,
                                                                                    _ => 100,
                                                                                };

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "?";

        var mb = bytes / 1024.0 / 1024;
        return mb >= 1024 ? $"{mb / 1024:0.##} GB" : $"{mb:0.#} MB";
    }

    public static string FormatDuration(double seconds)
    {
        if (seconds < 1)
            return "<1s";
        if (seconds < 90)
            return $"~{seconds:0}s";
        if (seconds < 90 * 60)
            return $"~{seconds / 60:0} min";

        return $"~{seconds / 3600:0.#} h";
    }
}

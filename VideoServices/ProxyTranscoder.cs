#nullable enable
using System;
using System.IO;
using System.Threading;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;
using T3.Core.Logging;
using T3.Core.Video;

namespace T3.VideoServices;

/// <summary>
/// Generates a seek-friendly proxy of a video by transcoding it file→file: decode the source (software) and
/// re-encode every frame to an all-intra codec (ProRes / HAP), optionally downscaled. CPU-byte-level — no D3D
/// device and no render pipeline — so it runs on a background worker and is unit-testable. Reuses the
/// render-export encoder (<see cref="FfmpegVideoEncoderFactory"/>); a proxy never uses H.264/HEVC (libx264 =
/// GPL). Video-only — audio is not part of the preview proxy.
/// </summary>
public static class ProxyTranscoder
{
    /// <summary>Transcodes <paramref name="sourcePath"/> to <paramref name="proxyPath"/>. Blocking — call on a
    /// background thread. <paramref name="scale"/> is 0.1–1.0 of the source resolution. Returns null on success,
    /// otherwise an error message (and removes any partial output).</summary>
    public static unsafe string? Generate(string sourcePath, string proxyPath, VideoExportCodec proxyCodec,
                                          double scale, IProgress<double>? progress, CancellationToken cancel)
    {
        using var decoder = VideoDecoderSession.TryOpen(sourcePath, VideoPlaybackOptimization.FastSeeking, out var openError);
        if (decoder == null)
            return openError ?? $"Could not open source video '{sourcePath}'.";

        // Proxy dimensions: source × scale, rounded even (and to a ×4 block for HAP's DXT).
        var clampedScale = Math.Clamp(scale, 0.1, 1.0);
        var rawW = (int)Math.Round(decoder.Width * clampedScale) & ~1;
        var rawH = (int)Math.Round(decoder.Height * clampedScale) & ~1;
        var (proxyW, proxyH) = proxyCodec.RoundToEncoderBlock(Math.Max(2, rawW), Math.Max(2, rawH));

        var fps = decoder.FrameRate > 0 ? decoder.FrameRate : 30;

        // Encode to a temp sibling and atomically swap into place on success. The engine substitutes a proxy the
        // moment its file exists, but a MOV/MP4 has no playable moov atom until the muxer finalizes — so writing the
        // final path directly lets preview pick up a half-written file ("moov atom not found"). The swap also means a
        // crash mid-encode can't leave a broken-but-present proxy. Keeps the .mov extension so the muxer is inferred.
        var tempPath = Path.Combine(Path.GetDirectoryName(proxyPath) ?? ".",
                                    Path.GetFileNameWithoutExtension(proxyPath) + ".partial" + Path.GetExtension(proxyPath));

        var settings = new VideoExportSettings
                           {
                               FilePath = tempPath,
                               Width = proxyW,
                               Height = proxyH,
                               FrameRate = fps,
                               Codec = proxyCodec,
                               ExportAudio = false,
                           };

        var writer = new FfmpegVideoEncoderFactory().TryCreateWriter(settings, out var encodeError);
        if (writer == null)
            return encodeError ?? "Could not create the proxy encoder.";

        string? error = null;
        var converter = new VideoFrameConverter();
        var rgba = Frame.CreateVideo(proxyW, proxyH, AVPixelFormat.Rgba);
        rgba.EnsureBuffer(1);
        var frameIndex = 0;

        try
        {
            var totalFrames = decoder.DurationSeconds > 0 ? decoder.DurationSeconds * fps : 0;
            while (decoder.TryReadNextFrame(out _))
            {
                if (cancel.IsCancellationRequested)
                {
                    error = "Proxy generation cancelled.";
                    break;
                }

                // Decode is YUV at source size; scale + convert to the proxy's RGBA in one swscale pass.
                converter.ConvertFrame(decoder.CurrentFrame, rgba, SWS.Bilinear);
                var span = new ReadOnlySpan<byte>((void*)rgba.Data[0], proxyH * rgba.Linesize[0]);
                writer.AddVideoFrame(span, rgba.Linesize[0]);

                frameIndex++;
                if (totalFrames > 0)
                    progress?.Report(Math.Min(1.0, frameIndex / totalFrames));
            }

            if (error == null)
            {
                writer.Finish();
                progress?.Report(1.0);
            }
        }
        catch (Exception e)
        {
            error = "Proxy generation failed: " + e.Message;
        }
        finally
        {
            // Dispose closes the file handle — must happen before the swap can move it.
            writer.Dispose();
            rgba.Dispose();
            converter.Free();
        }

        // Swap the finished temp file into place only now that it's complete and closed.
        if (error == null)
        {
            try
            {
                File.Move(tempPath, proxyPath, overwrite: true);
                Log.Gated.VideoRender($"Proxy generated: {proxyPath} ({proxyW}×{proxyH} {proxyCodec}, {frameIndex} frames)");
            }
            catch (Exception e)
            {
                error = "Could not finalize proxy: " + e.Message;
            }
        }

        if (error != null)
            TryDelete(tempPath);

        return error;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception e)
        {
            Log.Debug($"Could not delete partial proxy '{path}': {e.Message}");
        }
    }
}

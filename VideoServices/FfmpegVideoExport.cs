using System;
using System.Threading;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using T3.Core.Logging;
using T3.Core.Video;

namespace T3.VideoServices;

/// <summary>
/// The FFmpeg implementation of <see cref="IVideoEncoderFactory"/>. Registered into the Core
/// <see cref="VideoExport.Factory"/> holder so the editor's render-export can reach it across the operator
/// load-context boundary (the editor cannot depend on this assembly directly).
/// </summary>
public sealed class FfmpegVideoEncoderFactory : IVideoEncoderFactory
{
    /// <summary>
    /// Publishes the factory to Core. Idempotent and cheap (no native FFmpeg load — that is deferred to
    /// <see cref="TryCreateWriter"/>). Called eagerly when the operator package loads so export works even
    /// when the rendered graph never used a video operator.
    /// </summary>
    public static void Register() => VideoExport.Factory ??= new FfmpegVideoEncoderFactory();

    public IVideoFileWriter? TryCreateWriter(VideoExportSettings settings, out string? error)
    {
        error = null;
        try
        {
            if (!FfmpegLibrary.EnsureInitialized())
            {
                error = FfmpegLibrary.StatusError ?? "FFmpeg is not available";
                return null;
            }

            return new FfmpegVideoFileWriter(BuildEncoderSettings(settings));
        }
        catch (Exception e)
        {
            error = "Failed to create the FFmpeg video encoder: " + e.Message;
            return null;
        }
    }

    public string? GenerateProxy(string sourcePath, string proxyPath, VideoExportCodec proxyCodec, double scale,
                                 IProgress<double>? progress, CancellationToken cancel)
        => ProxyTranscoder.Generate(sourcePath, proxyPath, proxyCodec, scale, progress, cancel);

    public VideoProxyAdvice EvaluateProxyNeed(string sourcePath)
    {
        var result = ProxyEligibility.Evaluate(sourcePath);
        var recommendation = result.Recommendation switch
                                 {
                                     ProxyEligibility.Recommendation.Recommended => VideoProxyRecommendation.Recommended,
                                     ProxyEligibility.Recommendation.NotNeeded   => VideoProxyRecommendation.NotNeeded,
                                     _                                           => VideoProxyRecommendation.Unknown,
                                 };
        return new VideoProxyAdvice(recommendation, result.KeyframeIntervalSeconds, result.Reason);
    }

    public bool TryProbeDurationSeconds(string sourcePath, out double seconds)
        => VideoMetadata.TryProbeDurationSeconds(sourcePath, out seconds);

    public IVideoThumbnailReader? TryCreateThumbnailReader(string sourcePath, out string? error)
        => VideoThumbnailReader.TryOpen(sourcePath, out error);

    public VideoEncoderAvailability GetAvailability(VideoExportCodec codec)
    {
        if (!FfmpegLibrary.EnsureInitialized())
            return new VideoEncoderAvailability { Kind = VideoEncoderKind.Unavailable };

        if (codec == VideoExportCodec.H264)
        {
            // H.264: a GPU encoder when available, otherwise in-process software OpenH264 (no GPL). Either way
            // it encodes — there's no unavailable case (MPEG-4 backs up OpenH264 in the encode path).
            var hardwareEncoder = HardwareEncoderProbe.H264HardwareEncoder;
            if (hardwareEncoder != null)
                return new VideoEncoderAvailability { Kind = VideoEncoderKind.Hardware, EncoderName = FriendlyHardwareName(hardwareEncoder) };

            var softwareName = SoftwareH264EncoderName() != null ? "OpenH264" : "MPEG-4";
            return new VideoEncoderAvailability { Kind = VideoEncoderKind.Software, EncoderName = softwareName };
        }

        if (codec == VideoExportCodec.Hevc)
        {
            // HEVC: a GPU encoder when available, otherwise software libkvazaar (BSD, no GPL). No universal
            // last-resort fallback like H.264's MPEG-4, so it's Unavailable if neither is present.
            var hardwareEncoder = HardwareEncoderProbe.HevcHardwareEncoder;
            if (hardwareEncoder != null)
                return new VideoEncoderAvailability { Kind = VideoEncoderKind.Hardware, EncoderName = FriendlyHardwareName(hardwareEncoder) };

            return Codec.FindEncoderByName("libkvazaar") != null
                       ? new VideoEncoderAvailability { Kind = VideoEncoderKind.Software, EncoderName = "kvazaar" }
                       : new VideoEncoderAvailability { Kind = VideoEncoderKind.Unavailable };
        }

        // The rest are LGPL software encoders — but only if this build actually ships them. Probe before
        // claiming availability rather than failing at export time (encoder sets differ between FFmpeg builds).
        return SoftwareEncoderIsPresent(codec)
                   ? new VideoEncoderAvailability { Kind = VideoEncoderKind.Software, EncoderName = SoftwareEncoderLabel(codec) }
                   : new VideoEncoderAvailability { Kind = VideoEncoderKind.Unavailable, EncoderName = SoftwareEncoderLabel(codec) };
    }

    // OpenH264 (libopenh264 — Cisco's BSD-licensed software H.264) when the build ships it; null falls the
    // encode path back to MPEG-4. Far better than MPEG-4, and no GPL (libx264) needed.
    private static string? SoftwareH264EncoderName()
        => Codec.FindEncoderByName("libopenh264") != null ? "libopenh264" : null;

    // Whether the bundled FFmpeg build includes the encoder a given software codec maps to.
    private static bool SoftwareEncoderIsPresent(VideoExportCodec codec)
    {
        if (codec == VideoExportCodec.ProRes)
        {
            try
            {
                Codec.FindEncoderById(AVCodecID.Prores);
                return true;
            }
            catch
            {
                return false;
            }
        }

        var encoderName = codec switch
                              {
                                  VideoExportCodec.VP9 => "libvpx-vp9",
                                  VideoExportCodec.AV1 => "libsvtav1",
                                  VideoExportCodec.FFV1 => "ffv1",
                                  VideoExportCodec.Hap or VideoExportCodec.HapAlpha or VideoExportCodec.HapQ => "hap",
                                  _ => null,
                              };
        return encoderName != null && Codec.FindEncoderByName(encoderName) != null;
    }

    private static string FriendlyHardwareName(string encoderName) => encoderName switch
                                                                          {
                                                                              "h264_nvenc" or "hevc_nvenc" => "NVIDIA NVENC",
                                                                              "h264_qsv" or "hevc_qsv" => "Intel Quick Sync",
                                                                              "h264_amf" or "hevc_amf" => "AMD AMF",
                                                                              _ => encoderName,
                                                                          };

    private static string SoftwareEncoderLabel(VideoExportCodec codec) => codec switch
                                                                              {
                                                                                  VideoExportCodec.ProRes => "ProRes 422",
                                                                                  VideoExportCodec.VP9 => "VP9 (libvpx)",
                                                                                  VideoExportCodec.AV1 => "AV1 (SVT-AV1)",
                                                                                  VideoExportCodec.FFV1 => "FFV1",
                                                                                  VideoExportCodec.Hap => "HAP",
                                                                                  VideoExportCodec.HapAlpha => "HAP Alpha",
                                                                                  VideoExportCodec.HapQ => "HAP Q",
                                                                                  _ => codec.ToString(),
                                                                              };

    private static VideoEncoderSettings BuildEncoderSettings(VideoExportSettings s)
    {
        var fps = Math.Max(1, (int)Math.Round(s.FrameRate)); // integer rate, matching the previous MF writer

        var common = new VideoEncoderSettings
                         {
                             FilePath = s.FilePath,
                             Width = s.Width,
                             Height = s.Height,
                             FrameRate = new AVRational(fps, 1),
                             BitRate = s.BitRate,
                             SourceFormat = AVPixelFormat.Rgba,
                             SourceBytesPerPixel = 4,
                             EncodeAudio = s.ExportAudio,
                             AudioSampleRate = s.AudioSampleRate,
                             AudioChannels = s.AudioChannels,
                             AudioBitRate = 192_000,
                         };

        switch (s.Codec)
        {
            case VideoExportCodec.ProRes:
            {
                // ProRes 422 is an all-intra LGPL editing codec; it needs 10-bit 4:2:2 and sets its own rate.
                return common with
                           {
                               VideoCodecId = AVCodecID.Prores,
                               EncoderPixelFormat = AVPixelFormat.Yuv422p10le,
                           };
            }

            case VideoExportCodec.VP9:
            {
                // VP9 (libvpx) in MP4 — efficient but software-encoded. Default libvpx is single-threaded and
                // painfully slow; row-mt + a faster cpu-used make it usable (with ThreadCount=0 in the encoder).
                return common with
                           {
                               VideoEncoderName = "libvpx-vp9",
                               EncoderPixelFormat = AVPixelFormat.Yuv420p,
                               VideoCodecOptions = new[]
                                                       {
                                                           new KeyValuePair<string, string>("deadline", "good"),
                                                           new KeyValuePair<string, string>("cpu-used", "4"),
                                                           new KeyValuePair<string, string>("row-mt", "1"),
                                                       },
                           };
            }

            case VideoExportCodec.AV1:
            {
                // AV1 via SVT-AV1 in MP4 — the most efficient delivery codec. `preset` trades speed for size
                // (0 slowest … 13 fastest); 8 is a usable export default rather than SVT's slow default.
                return common with
                           {
                               VideoEncoderName = "libsvtav1",
                               EncoderPixelFormat = AVPixelFormat.Yuv420p,
                               VideoCodecOptions = new[] { new KeyValuePair<string, string>("preset", "8") },
                           };
            }

            case VideoExportCodec.FFV1:
            {
                // FFV1 in MKV — a lossless intra archival codec; large files, ignores the target bitrate.
                return common with
                           {
                               VideoEncoderName = "ffv1",
                               EncoderPixelFormat = AVPixelFormat.Yuv420p,
                           };
            }

            case VideoExportCodec.Hevc:
            {
                // Hardware HEVC when the GPU supports it; otherwise software libkvazaar (BSD, not GPL libx265).
                var hardwareEncoder = HardwareEncoderProbe.HevcHardwareEncoder;
                return common with
                           {
                               VideoEncoderName = hardwareEncoder ?? "libkvazaar",
                               EncoderPixelFormat = HardwareEncoderProbe.EncoderInputFormat(hardwareEncoder),
                           };
            }

            case VideoExportCodec.Hap:
                return BuildHapSettings(common, "hap");

            case VideoExportCodec.HapAlpha:
                return BuildHapSettings(common, "hap_alpha");

            case VideoExportCodec.HapQ:
                return BuildHapSettings(common, "hap_q");

            case VideoExportCodec.H264:
            default:
            {
                // Hardware H.264 when the GPU supports it; otherwise in-process OpenH264 (libopenh264 — BSD, ships
                // in the LGPL build, no GPL). MPEG-4 Part 2 is only a last resort if OpenH264 is somehow absent.
                // (libx264 is GPL and intentionally not bundled.)
                var hardwareEncoder = HardwareEncoderProbe.H264HardwareEncoder;
                var encoderName = hardwareEncoder ?? SoftwareH264EncoderName();
                if (encoderName == null)
                    Log.Warning("No hardware or OpenH264 encoder available - exporting with MPEG-4 (lower quality).");

                return common with
                           {
                               VideoEncoderName = encoderName, // null => fall back to VideoCodecId (MPEG-4)
                               VideoCodecId = AVCodecID.Mpeg4,
                               // QSV needs nv12; NVENC/OpenH264/MPEG-4 take yuv420p. Must match what the probe opened with.
                               EncoderPixelFormat = HardwareEncoderProbe.EncoderInputFormat(hardwareEncoder),
                           };
            }
        }
    }

    private static VideoEncoderSettings BuildHapSettings(VideoEncoderSettings common, string hapFormat)
    {
        // HAP feeds RGBA straight in and snappy-compresses DXT blocks itself, so it ignores the target bitrate.
        // DXT works on 4×4 blocks, so the frame size must be a multiple of 4 — round down (crops ≤ 3 px) rather
        // than let the encoder reject odd sizes.
        var (width, height) = VideoExportCodec.Hap.RoundToEncoderBlock(common.Width, common.Height);
        return common with
                   {
                       VideoEncoderName = "hap",
                       EncoderPixelFormat = AVPixelFormat.Rgba,
                       Width = width,
                       Height = height,
                       VideoCodecOptions = new[] { new KeyValuePair<string, string>("format", hapFormat) },
                   };
    }
}

/// <summary>Thin Core-facing wrapper over <see cref="VideoFileEncoder"/>.</summary>
internal sealed class FfmpegVideoFileWriter : IVideoFileWriter
{
    public FfmpegVideoFileWriter(in VideoEncoderSettings settings) => _encoder = new VideoFileEncoder(settings);

    public void AddVideoFrame(ReadOnlySpan<byte> rgbaPixels, int rowStride) => _encoder.WriteVideoFrame(rgbaPixels, rowStride);
    public void AddAudioSamples(ReadOnlySpan<byte> interleavedFloatPcm) => _encoder.WriteAudioSamples(interleavedFloatPcm);
    public void Finish() => _encoder.Finish();
    public void Dispose() => _encoder.Dispose();

    private readonly VideoFileEncoder _encoder;
}

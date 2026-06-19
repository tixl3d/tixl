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

    public VideoEncoderAvailability GetAvailability(VideoExportCodec codec)
    {
        if (!FfmpegLibrary.EnsureInitialized())
            return new VideoEncoderAvailability { Kind = VideoEncoderKind.Unavailable };

        if (codec == VideoExportCodec.H264)
        {
            // H.264 is dynamic: software H.264 (libx264) is GPL and absent from the bundled LGPL build, so it
            // needs a GPU encoder, else the MPEG-4 fallback stands in until a user-supplied GPL ffmpeg exists.
            var hardwareEncoder = HardwareEncoderProbe.H264HardwareEncoder;
            return hardwareEncoder == null
                       ? new VideoEncoderAvailability { Kind = VideoEncoderKind.SoftwareFallback, EncoderName = "MPEG-4" }
                       : new VideoEncoderAvailability { Kind = VideoEncoderKind.Hardware, EncoderName = FriendlyHardwareName(hardwareEncoder) };
        }

        // The rest are LGPL software encoders — but only if this build actually ships them. The BtbN
        // lgpl-shared build omits some native encoders (notably HAP, decode-only here), so probe before
        // claiming availability rather than failing at export time.
        return SoftwareEncoderIsPresent(codec)
                   ? new VideoEncoderAvailability { Kind = VideoEncoderKind.Software, EncoderName = SoftwareEncoderLabel(codec) }
                   : new VideoEncoderAvailability { Kind = VideoEncoderKind.Unavailable, EncoderName = SoftwareEncoderLabel(codec) };
    }

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
                // VP9 (libvpx) in MP4 — an efficient delivery codec; software-encoded, so slower than H.264.
                return common with
                           {
                               VideoEncoderName = "libvpx-vp9",
                               EncoderPixelFormat = AVPixelFormat.Yuv420p,
                           };
            }

            case VideoExportCodec.AV1:
            {
                // AV1 via SVT-AV1 in MP4 — the most efficient delivery codec; SVT is the fast software AV1 encoder.
                return common with
                           {
                               VideoEncoderName = "libsvtav1",
                               EncoderPixelFormat = AVPixelFormat.Yuv420p,
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

            case VideoExportCodec.Hap:
                return BuildHapSettings(common, "hap");

            case VideoExportCodec.HapAlpha:
                return BuildHapSettings(common, "hap_alpha");

            case VideoExportCodec.HapQ:
                return BuildHapSettings(common, "hap_q");

            case VideoExportCodec.H264:
            default:
            {
                // Hardware H.264 when the GPU supports it; otherwise MPEG-4 Part 2. Software H.264 is libx264 =
                // GPL and absent from the bundled LGPL build, so the fallback stands in until a user-supplied GPL
                // ffmpeg path is available.
                var hardwareEncoder = HardwareEncoderProbe.H264HardwareEncoder;
                if (hardwareEncoder == null)
                    Log.Warning("No hardware H.264 encoder available - exporting with MPEG-4 (lower quality).");

                return common with
                           {
                               VideoEncoderName = hardwareEncoder, // null => fall back to VideoCodecId
                               VideoCodecId = AVCodecID.Mpeg4,
                               EncoderPixelFormat = AVPixelFormat.Yuv420p,
                           };
            }
        }
    }

    private static VideoEncoderSettings BuildHapSettings(VideoEncoderSettings common, string hapFormat)
    {
        // HAP feeds RGBA straight in and snappy-compresses DXT blocks itself, so it ignores the target bitrate.
        // DXT works on 4×4 blocks, so the frame size must be a multiple of 4 — round down (crops ≤ 3 px) rather
        // than let the encoder reject odd sizes.
        return common with
                   {
                       VideoEncoderName = "hap",
                       EncoderPixelFormat = AVPixelFormat.Rgba,
                       Width = common.Width & ~3,
                       Height = common.Height & ~3,
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

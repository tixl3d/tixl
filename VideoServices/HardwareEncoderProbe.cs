using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;
using T3.Core.Logging;

namespace T3.VideoServices;

/// <summary>
/// Picks this machine's best available H.264/HEVC <em>hardware</em> encoder by actually opening each
/// candidate — being compiled into the build says nothing about the GPU/driver supporting it (e.g. a build
/// ships <c>h264_qsv</c>/<c>h264_amf</c> wrappers, but they fail to init without an Intel/AMD device). Results
/// are cached. When none works, the caller falls back to an LGPL software codec (software H.264/HEVC is
/// libx264/libx265 = GPL, absent from the shipped build) or the user-supplied GPL <c>ffmpeg.exe</c> (tier 2).
/// </summary>
public static class HardwareEncoderProbe
{
    // Vendor order: NVIDIA NVENC, Intel Quick Sync, AMD AMF; Apple's VideoToolbox first on macOS.
    // The first that opens wins. (VAAPI is deliberately absent: its encoders need a hardware-frame
    // pipeline this encode path doesn't have, unlike NVENC/QSV/AMF/VideoToolbox which accept
    // system-memory frames.)
    private static readonly string[] H264Candidates = OperatingSystem.IsMacOS()
                                                          ? ["h264_videotoolbox", "h264_nvenc", "h264_qsv", "h264_amf"]
                                                          : ["h264_nvenc", "h264_qsv", "h264_amf"];
    private static readonly string[] HevcCandidates = OperatingSystem.IsMacOS()
                                                          ? ["hevc_videotoolbox", "hevc_nvenc", "hevc_qsv", "hevc_amf"]
                                                          : ["hevc_nvenc", "hevc_qsv", "hevc_amf"];

    /// <summary>The working H.264 hardware encoder name (e.g. <c>h264_nvenc</c>), or null if none initializes.</summary>
    public static string? H264HardwareEncoder => _h264 ??= FindWorkingEncoder(H264Candidates);

    /// <summary>The working HEVC hardware encoder name, or null if none initializes.</summary>
    public static string? HevcHardwareEncoder => _hevc ??= FindWorkingEncoder(HevcCandidates);

    /// <summary>
    /// The encoder-input pixel format a given encoder accepts. Quick Sync (<c>*_qsv</c>) only takes <c>nv12</c>
    /// (or a hardware <c>qsv</c> surface); NVENC and the software/AMF encoders accept planar <c>yuv420p</c>.
    /// Used by both the probe and the real encode so they agree — feeding QSV <c>yuv420p</c> fails to open.
    /// </summary>
    public static AVPixelFormat EncoderInputFormat(string? encoderName)
        => encoderName != null && encoderName.Contains("_qsv") ? AVPixelFormat.Nv12 : AVPixelFormat.Yuv420p;

    private static string? FindWorkingEncoder(string[] candidates)
    {
        if (!FfmpegLibrary.EnsureInitialized())
            return null;

        foreach (var name in candidates)
        {
            if (!CanOpenEncoder(name))
                continue;

            Log.Debug($"Hardware video encoder available: {name}");
            return name;
        }

        return null;
    }

    // Opening is not proof of encoding: a headless VM can initialize VideoToolbox/QSV and then emit no
    // packets at all. Encode keyframes with the same settings the live encoder uses and require a packet
    // without flushing — a live stream never flushes, so an encoder that only emits on flush is unusable.
    private static unsafe bool CanOpenEncoder(string encoderName)
    {
        CodecContext? context = null;
        try
        {
            var codec = Codec.FindEncoderByName(encoderName);
            var pixelFormat = EncoderInputFormat(encoderName);
            context = new CodecContext(codec)
                          {
                              Width = 320,
                              Height = 240,
                              TimeBase = new AVRational(1, 30),
                              Framerate = new AVRational(30, 1),
                              PixelFormat = pixelFormat,
                              BitRate = 1_500_000,
                              MaxBFrames = 0,
                              GopSize = 60,
                              ColorRange = AVColorRange.Mpeg,
                              ColorPrimaries = AVColorPrimaries.Bt709,
                              ColorTrc = AVColorTransferCharacteristic.Bt709,
                              Colorspace = AVColorSpace.Bt709,
                          };
            context.Flags |= AV_CODEC_FLAG.LowDelay;
            LiveH264Encoder.ApplyLowLatencyOptions(context, encoderName);
            context.Open();

            using var frame = Frame.CreateVideo(320, 240, pixelFormat);
            frame.EnsureBuffer(1);
            frame.ColorRange = AVColorRange.Mpeg;
            frame.ColorPrimaries = AVColorPrimaries.Bt709;
            frame.ColorTrc = AVColorTransferCharacteristic.Bt709;
            frame.Colorspace = AVColorSpace.Bt709;

            using var packet = new Packet();
            for (var i = 0; i < 3; i++)
            {
                frame.Pts = i;
                frame.PictType = i == 0 ? AVPictureType.I : AVPictureType.None;
                context.SendFrame(frame);
                if (context.ReceivePacket(packet) != CodecResult.Success)
                    continue;

                packet.Unref();
                return true;
            }

            Log.Debug($"Hardware video encoder {encoderName} opened but produced no packet for 3 frames");
            return false;
        }
        catch (Exception e)
        {
            Log.Debug($"Hardware video encoder {encoderName} not usable: {e.Message}");
            return false;
        }
        finally
        {
            context?.Dispose();
        }
    }

    private static string? _h264;
    private static string? _hevc;
}

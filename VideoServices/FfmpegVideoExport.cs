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

    private static VideoEncoderSettings BuildEncoderSettings(VideoExportSettings s)
    {
        // H.264 via a hardware encoder when the GPU supports it; otherwise an LGPL software fallback. Software
        // H.264 is libx264 = GPL and absent from the shipped build, so MPEG-4 Part 2 stands in until the codec
        // selector (Phase 2) and the user-supplied GPL ffmpeg.exe path (Phase 4) land.
        var hardwareEncoder = HardwareEncoderProbe.H264HardwareEncoder;
        if (hardwareEncoder == null)
            Log.Warning("No hardware H.264 encoder available - exporting with MPEG-4 (lower quality). "
                        + "A later update adds a codec selector and software H.264.");

        var fps = Math.Max(1, (int)Math.Round(s.FrameRate)); // integer rate, matching the previous MF writer

        return new VideoEncoderSettings
                   {
                       FilePath = s.FilePath,
                       Width = s.Width,
                       Height = s.Height,
                       FrameRate = new AVRational(fps, 1),
                       BitRate = s.BitRate,
                       VideoEncoderName = hardwareEncoder, // null => fall back to VideoCodecId
                       VideoCodecId = AVCodecID.Mpeg4,
                       SourceFormat = AVPixelFormat.Rgba,
                       SourceBytesPerPixel = 4,
                       EncodeAudio = s.ExportAudio,
                       AudioSampleRate = s.AudioSampleRate,
                       AudioChannels = s.AudioChannels,
                       AudioBitRate = 192_000,
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

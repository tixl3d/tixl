#nullable enable
using System;

namespace T3.Core.Video;

/// <summary>The video codec for a render-export; the video assembly maps each to an FFmpeg encoder + container.</summary>
public enum VideoExportCodec
{
    /// <summary>H.264 in MP4 — hardware-encoded where available, else an LGPL software fallback. The default.</summary>
    H264 = 0,

    /// <summary>Apple ProRes 422 in MOV — an all-intra editing codec (larger files); always available (LGPL).</summary>
    ProRes = 1,

    /// <summary>VP9 in MP4 — an efficient delivery codec, software-encoded (libvpx, slower than H.264).</summary>
    VP9 = 2,

    /// <summary>AV1 in MP4 — the most efficient delivery codec, software-encoded (SVT-AV1).</summary>
    AV1 = 3,

    /// <summary>FFV1 in MKV — a lossless intra archival codec (very large files).</summary>
    FFV1 = 4,

    /// <summary>HAP (DXT1, RGB) in MOV — a GPU-friendly intra codec for realtime/VJ playback; no alpha. (LGPL.)</summary>
    Hap = 5,

    /// <summary>HAP Alpha (DXT5) in MOV — HAP carrying an alpha channel. (LGPL.)</summary>
    HapAlpha = 6,

    /// <summary>HAP Q (scaled DXT5-YCoCg) in MOV — higher-quality HAP; larger than standard HAP. (LGPL.)</summary>
    HapQ = 7,
}

/// <summary>Container/extension helpers for <see cref="VideoExportCodec"/>.</summary>
public static class VideoExportCodecExtensions
{
    /// <summary>The output-file extension (which selects the container) for a codec.</summary>
    public static string GetFileExtension(this VideoExportCodec codec) => codec switch
                                                                              {
                                                                                  VideoExportCodec.ProRes => ".mov",
                                                                                  VideoExportCodec.Hap => ".mov",
                                                                                  VideoExportCodec.HapAlpha => ".mov",
                                                                                  VideoExportCodec.HapQ => ".mov",
                                                                                  VideoExportCodec.FFV1 => ".mkv",
                                                                                  _ => ".mp4", // H264, VP9, AV1
                                                                              };
}

/// <summary>
/// FFmpeg-free description of a render-export target. The editor's render-export builds this; the video
/// assembly maps it onto its own FFmpeg settings. Kept in Core so the editor (which must not depend on the
/// FFmpeg/operator assembly) can describe an encode.
/// </summary>
public readonly record struct VideoExportSettings
{
    public required string FilePath { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Frames per second (rounded to an integer rate to match the previous Media Foundation writer).</summary>
    public required double FrameRate { get; init; }

    public long BitRate { get; init; }

    public bool ExportAudio { get; init; }
    public int AudioChannels { get; init; }
    public int AudioSampleRate { get; init; }

    /// <summary>Which codec/container to encode. Default <see cref="VideoExportCodec.H264"/>.</summary>
    public VideoExportCodec Codec { get; init; }
}

/// <summary>
/// Writes one render-export file. The editor reads back each output frame to CPU RGBA8 bytes (it owns the GPU
/// readback) and hands them here; the implementation (in the video assembly) encodes and muxes them.
/// </summary>
public interface IVideoFileWriter : IDisposable
{
    /// <summary>Encodes one frame. <paramref name="rgbaPixels"/> is RGBA8, <paramref name="rowStride"/> bytes
    /// per row (≥ Width*4); read only during the call.</summary>
    void AddVideoFrame(ReadOnlySpan<byte> rgbaPixels, int rowStride);

    /// <summary>Buffers interleaved 32-bit-float PCM for the audio track. No-op when audio is disabled.</summary>
    void AddAudioSamples(ReadOnlySpan<byte> interleavedFloatPcm);

    /// <summary>Flushes the encoders and writes the container trailer. <see cref="IDisposable.Dispose"/> also calls it.</summary>
    void Finish();
}

/// <summary>How a <see cref="VideoExportCodec"/> will be encoded on this machine — for inline UI before export.</summary>
public enum VideoEncoderKind
{
    /// <summary>FFmpeg / the video package isn't available, so the codec can't be encoded at all.</summary>
    Unavailable,

    /// <summary>A built-in LGPL software encoder serves it (ProRes/VP9/AV1/FFV1). Always available, no setup.</summary>
    Software,

    /// <summary>A GPU hardware encoder (NVENC/Quick Sync/AMF) initialised successfully (H.264). Preferred, silent.</summary>
    Hardware,

    /// <summary>Software H.264/HEVC is GPL and absent from the bundled build, and no hardware encoder works here,
    /// so a lower-quality LGPL substitute (MPEG-4) stands in until a user-supplied GPL ffmpeg is installed.</summary>
    SoftwareFallback,
}

/// <summary>How the requested codec will be served, for the render window's inline availability indicator.</summary>
public readonly record struct VideoEncoderAvailability
{
    public required VideoEncoderKind Kind { get; init; }

    /// <summary>A human-readable label for the encoder that will run (e.g. "NVIDIA NVENC", "MPEG-4"). May be null.</summary>
    public string? EncoderName { get; init; }
}

/// <summary>Creates <see cref="IVideoFileWriter"/>s; implemented by the video assembly.</summary>
public interface IVideoEncoderFactory
{
    /// <summary>Returns null and an <paramref name="error"/> message when no usable encoder can be created.</summary>
    IVideoFileWriter? TryCreateWriter(VideoExportSettings settings, out string? error);

    /// <summary>Reports how <paramref name="codec"/> will be encoded here. The hardware probe behind it is a
    /// one-shot GPU-encoder open (cached); callers on a UI thread should run this off-thread on first use.</summary>
    VideoEncoderAvailability GetAvailability(VideoExportCodec codec);
}

/// <summary>
/// Holds the process-wide video-encode factory. The video assembly sets it when its operator package loads
/// (it is the only assembly that can depend on FFmpeg); the editor's render-export reads it. Null until the
/// video assembly has registered — render-export surfaces a clear message in that case. Mirrors
/// <see cref="VideoPlayback"/>; Core is loaded once and shared across the editor and operator load contexts,
/// so this single static bridges them.
/// </summary>
public static class VideoExport
{
    public static IVideoEncoderFactory? Factory { get; set; }
}

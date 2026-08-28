#nullable enable
using System;
using System.Threading;

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

    /// <summary>HEVC (H.265) in MP4 — hardware-encoded where available, else software (libkvazaar — BSD, no GPL).
    /// More efficient than H.264; heavier patent licensing (orthogonal to the software licence).</summary>
    Hevc = 8,
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

    /// <summary>The size a codec's frame dimensions must be a multiple of. HAP packs 4×4 DXT blocks, so it needs
    /// 4; the others have no constraint the export path enforces (1).</summary>
    public static int GetEncoderBlockSize(this VideoExportCodec codec) => codec switch
                                                                              {
                                                                                  VideoExportCodec.Hap => 4,
                                                                                  VideoExportCodec.HapAlpha => 4,
                                                                                  VideoExportCodec.HapQ => 4,
                                                                                  _ => 1,
                                                                              };

    /// <summary>Rounds a render resolution down to the codec's block size — the dimensions the encoder actually
    /// outputs (the export path crops the few excess pixels). A no-op for codecs with no block constraint.</summary>
    public static (int width, int height) RoundToEncoderBlock(this VideoExportCodec codec, int width, int height)
    {
        var block = codec.GetEncoderBlockSize();
        if (block <= 1)
            return (width, height);

        return (Math.Max(block, width / block * block), Math.Max(block, height / block * block));
    }
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

    /// <summary>A built-in software encoder serves it in-process (ProRes/VP9/AV1/FFV1/HAP, or H.264 via OpenH264).</summary>
    Software,

    /// <summary>A GPU hardware encoder (NVENC/Quick Sync/AMF) initialised successfully (H.264). Preferred, silent.</summary>
    Hardware,
}

/// <summary>How the requested codec will be served, for the render window's inline availability indicator.</summary>
public readonly record struct VideoEncoderAvailability
{
    public required VideoEncoderKind Kind { get; init; }

    /// <summary>A human-readable label for the encoder that will run (e.g. "NVIDIA NVENC", "MPEG-4"). May be null.</summary>
    public string? EncoderName { get; init; }
}

/// <summary>Whether a video source benefits from a seek-proxy — decided from its measured keyframe spacing.</summary>
public enum VideoProxyRecommendation
{
    /// <summary>Already seek-cheap (all-intra or short-GOP); a proxy would waste disk and time.</summary>
    NotNeeded,

    /// <summary>Long-GOP inter-frame video; a proxy makes scrubbing/seeking cheap.</summary>
    Recommended,

    /// <summary>Couldn't tell (file unreadable, no video stream, no usable timestamps).</summary>
    Unknown,
}

/// <summary>The proxy advice for one source: the recommendation, the measured keyframe interval, and a
/// human-readable reason for UI tooltips.</summary>
public readonly record struct VideoProxyAdvice(VideoProxyRecommendation Recommendation,
                                               double KeyframeIntervalSeconds, string Reason);

/// <summary>Creates <see cref="IVideoFileWriter"/>s; implemented by the video assembly.</summary>
public interface IVideoEncoderFactory
{
    /// <summary>Returns null and an <paramref name="error"/> message when no usable encoder can be created.</summary>
    IVideoFileWriter? TryCreateWriter(VideoExportSettings settings, out string? error);

    /// <summary>Reports how <paramref name="codec"/> will be encoded here. The hardware probe behind it is a
    /// one-shot GPU-encoder open (cached); callers on a UI thread should run this off-thread on first use.</summary>
    VideoEncoderAvailability GetAvailability(VideoExportCodec codec);

    /// <summary>Transcodes a source video to an all-intra **proxy** file (decode → re-encode at
    /// <paramref name="scale"/> of the source resolution) for fast seeking. Blocking and CPU-only — the editor
    /// calls it on a background thread. Returns null on success, otherwise an error message.</summary>
    string? GenerateProxy(string sourcePath, string proxyPath, VideoExportCodec proxyCodec, double scale,
                          IProgress<double>? progress, CancellationToken cancel);

    /// <summary>Reports whether <paramref name="sourcePath"/> is worth proxying, from its measured keyframe
    /// spacing (demux-only — no decode). Used to decide auto-generation and to hint the manual trigger.</summary>
    VideoProxyAdvice EvaluateProxyNeed(string sourcePath);

    /// <summary>Reads <paramref name="sourcePath"/>'s duration in seconds (demux-only — no decode). Returns false
    /// (and 0) when the file can't be read or carries no usable duration. Used to size a freshly dropped video
    /// timeline clip to its real length.</summary>
    bool TryProbeDurationSeconds(string sourcePath, out double seconds);

    /// <summary>Opens <paramref name="sourcePath"/> for single-frame thumbnail grabs. Returns null and an
    /// <paramref name="error"/> when the file can't be decoded. The reader owns its own decoder session —
    /// independent of any playback stream and its frame cache — and must be used from one thread only.</summary>
    IVideoThumbnailReader? TryCreateThumbnailReader(string sourcePath, out string? error);
}

/// <summary>
/// Reads single video frames as small RGBA images for timeline thumbnails. Each read is a blocking
/// keyframe-seek + decode-forward (up to one GOP of software decode), so callers run it on a background
/// worker, never on the draw thread.
/// </summary>
public interface IVideoThumbnailReader : IDisposable
{
    double DurationSeconds { get; }

    /// <summary>Decodes the frame at <paramref name="seconds"/> (clamped to the stream) and writes it
    /// aspect-fitted and centered into <paramref name="rgbaBuffer"/> (RGBA8, <paramref name="targetWidth"/> ×
    /// <paramref name="targetHeight"/> pixels, letterbox areas transparent). Returns false on decode failure.</summary>
    bool TryReadFrame(double seconds, int targetWidth, int targetHeight, byte[] rgbaBuffer);
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

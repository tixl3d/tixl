#nullable enable
using System;
using System.IO;
using T3.Core.DataTypes;

namespace T3.Core.Video;

/// <summary>
/// How a video stream trades decode work against seek responsiveness, chosen per operator and passed to
/// <see cref="IVideoPlaybackEngine.RequestFrame"/>.
/// </summary>
public enum VideoPlaybackOptimization
{
    /// <summary>
    /// Software decode with a RAM frame cache: instant scrub-back / loop re-seeks and smooth playback (no
    /// per-frame GPU read-back), keeping the GPU free for the editor. Heavier CPU cost on large frames.
    /// </summary>
    FastSeeking = 0,

    /// <summary>
    /// Zero-copy GPU (hardware) decode, no cache: smoothest continuous playback — best for large/4K frames — but
    /// random seeks re-decode the GOP. Falls back to software when the codec/GPU profile is unsupported.
    /// </summary>
    PlaybackPerformance = 1,
}

/// <summary>
/// The latest frame and status for one video stream, returned by
/// <see cref="IVideoPlaybackEngine.RequestFrame"/>. <see cref="Produced"/> is true when this call uploaded a
/// new frame; <see cref="IsReady"/> drives the export-gated <c>Playback.OpNotReady</c>.
/// </summary>
public readonly record struct VideoFrameResult(
    bool Produced,
    Texture2D? Texture,
    float Duration,
    bool HasCompleted,
    bool IsReady,
    string? ErrorMessage);

/// <summary>
/// Process-wide video-playback service. The implementation lives in the operator-loaded video assembly; this
/// Core interface lets operators (and, later, the editor) reach it without depending on FFmpeg. One instance
/// owns every decode stream and its frame cache. Obtain it via <see cref="VideoPlayback.Engine"/>.
/// </summary>
public interface IVideoPlaybackEngine
{
    /// <summary>
    /// Drives the stream identified by <paramref name="streamId"/> toward <paramref name="requestedSeconds"/>
    /// and returns its latest frame and status. The stream's decoder is created on first request and re-opened
    /// if <paramref name="optimization"/> changes.
    /// </summary>
    VideoFrameResult RequestFrame(Guid streamId, string absolutePath, double requestedSeconds, bool loop,
                                  bool renderingToFile, VideoPlaybackOptimization optimization);

    /// <summary>
    /// Drives the stream's audio track toward <paramref name="sourceSeconds"/> and returns its BASS channel
    /// handle for the audio graph to route — 0 while the track is still opening, or when the file has no audio.
    /// <paramref name="absolutePath"/> must be the original file: a preview proxy carries no audio track.
    /// <para>Separate from <see cref="RequestFrame"/> on purpose. Audio decodes on its own demuxer and its own
    /// cadence, and callers must be able to keep pulling frames while staying silent — a timeline clip
    /// pre-rolls its decoder before its cut.</para>
    /// <para><paramref name="renderingToFile"/> switches to deterministic feeding: the audio is decoded
    /// synchronously before this call returns, advancing one exported frame per call rather than with the wall
    /// clock, so the caller can pull a repeatable mixdown straight afterwards.</para>
    /// </summary>
    int RequestAudio(Guid streamId, string absolutePath, double sourceSeconds, bool loop, bool renderingToFile);

    /// <summary>Releases the stream's decoder, cache and audio track. Call when the owning operator is disposed.</summary>
    void ReleaseStream(Guid streamId);
}

/// <summary>
/// Holds the process-wide <see cref="IVideoPlaybackEngine"/>. The video assembly sets it when it first loads;
/// it stays null in a project that never uses video (the video assembly is never loaded).
/// </summary>
public static class VideoPlayback
{
    public static IVideoPlaybackEngine? Engine { get; set; }

    /// <summary>Sibling-file suffix that marks a generated proxy (<c>clip</c> → <c>clip.proxy.mov</c>). Proxy codecs
    /// (ProRes/HAP/…) all mux to MOV, so a single suffix identifies every proxy for lookup and cleanup.</summary>
    public const string ProxySuffix = ".proxy.mov";

    /// <summary>The sibling proxy path for a source video (<c>clip.mp4</c> → <c>clip.proxy.mov</c>). Shared by
    /// proxy generation and the engine's substitution so both agree.</summary>
    public static string GetProxyPath(string sourcePath)
        => Path.Combine(Path.GetDirectoryName(sourcePath) ?? ".",
                        Path.GetFileNameWithoutExtension(sourcePath) + ProxySuffix);
}

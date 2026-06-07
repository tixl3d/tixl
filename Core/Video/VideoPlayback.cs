#nullable enable
using System;
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

    /// <summary>Releases the stream's decoder and cache. Call when the owning operator is disposed.</summary>
    void ReleaseStream(Guid streamId);
}

/// <summary>
/// Holds the process-wide <see cref="IVideoPlaybackEngine"/>. The video assembly sets it when it first loads;
/// it stays null in a project that never uses video (the video assembly is never loaded).
/// </summary>
public static class VideoPlayback
{
    public static IVideoPlaybackEngine? Engine { get; set; }
}

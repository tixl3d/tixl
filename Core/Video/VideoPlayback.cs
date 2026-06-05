#nullable enable
using System;
using T3.Core.DataTypes;

namespace T3.Core.Video;

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
    /// and returns its latest frame and status. The stream's decoder is created on first request.
    /// </summary>
    VideoFrameResult RequestFrame(Guid streamId, string absolutePath, double requestedSeconds, bool loop, bool renderingToFile);

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

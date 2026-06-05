using System.Collections.Generic;
using T3.Core.Video;

namespace T3.Video;

/// <summary>
/// The global <see cref="IVideoPlaybackEngine"/>. Operators are thin clients: each posts its per-frame
/// request keyed by a stable stream id, and the engine owns the underlying
/// <see cref="VideoPlaybackController"/>s (decode workers + frame caches). It caps how many streams stay live
/// and shares one cache budget across them, so a timeline with hundreds of clips never means hundreds of
/// decoders.
///
/// Accessed only from the operator evaluation thread (graph eval is single-threaded), so the stream map needs
/// no locking.
/// </summary>
public sealed class VideoPlaybackEngine : IVideoPlaybackEngine
{
    private sealed class Stream(VideoPlaybackController controller)
    {
        public readonly VideoPlaybackController Controller = controller;
        public long LastRequestMs;
    }

    /// <summary>The singleton; creating it also publishes it to <see cref="VideoPlayback.Engine"/>.</summary>
    public static VideoPlaybackEngine Instance => _instance ??= Register();

    public VideoFrameResult RequestFrame(Guid streamId, string absolutePath, double requestedSeconds, bool loop, bool renderingToFile)
    {
        var now = Environment.TickCount64;
        if (!_streams.TryGetValue(streamId, out var stream))
        {
            stream = new Stream(new VideoPlaybackController()) { LastRequestMs = now };
            _streams[streamId] = stream;
            RedistributeBudget();
        }

        stream.LastRequestMs = now;
        EvictStaleStreams(now);

        var controller = stream.Controller;
        var produced = controller.Update(absolutePath, requestedSeconds, loop, renderingToFile);
        return new VideoFrameResult(produced, controller.Texture, controller.Duration, controller.HasCompleted,
                                    controller.IsReady, controller.ErrorMessage);
    }

    public void ReleaseStream(Guid streamId)
    {
        if (!_streams.Remove(streamId, out var stream))
            return;

        stream.Controller.Dispose();
        RedistributeBudget();
    }

    // Frees streams the graph no longer drives: any idle past the timeout, plus — when above the live cap —
    // the most-idle ones past a short grace, down to the cap. A genuinely active stream (requested within the
    // grace) is never evicted, so more-than-cap simultaneously-visible clips degrade by exceeding the cap
    // rather than thrashing decoders. Runs opportunistically: the engine is only ticked from RequestFrame, so
    // when nothing requests there is also no resource pressure.
    private void EvictStaleStreams(long now)
    {
        var evictedAny = false;
        while (TryFindEvictable(now, out var evictId))
        {
            _streams[evictId].Controller.Dispose();
            _streams.Remove(evictId);
            evictedAny = true;
        }

        if (evictedAny)
            RedistributeBudget();
    }

    private bool TryFindEvictable(long now, out Guid evictId)
    {
        evictId = default;
        var overCap = _streams.Count > MaxLiveStreams;
        var oldest = long.MaxValue;
        foreach (var (id, stream) in _streams)
        {
            var idleMs = now - stream.LastRequestMs;
            var evictable = idleMs > IdleTimeoutMs || (overCap && idleMs > IdleGraceMs);
            if (!evictable || stream.LastRequestMs >= oldest)
                continue;

            oldest = stream.LastRequestMs;
            evictId = id;
        }

        return oldest != long.MaxValue;
    }

    // Splits the shared cache budget evenly across live streams: a lone video gets the whole budget; more
    // streams each get a smaller share. (Activity-weighted shares are a later refinement.)
    private void RedistributeBudget()
    {
        var count = _streams.Count;
        if (count == 0)
            return;

        var perStream = GlobalCacheBudgetBytes / count;
        foreach (var stream in _streams.Values)
            stream.Controller.SetCacheBudget(perStream);
    }

    private static VideoPlaybackEngine Register()
    {
        var engine = new VideoPlaybackEngine();
        VideoPlayback.Engine = engine;
        return engine;
    }

    // Caps and budget become project settings later (export-safe); the bounded-pool defaults match the
    // observed concurrency (mostly 0-3 streams, rarely >7).
    private const int MaxLiveStreams = 8;
    private const long IdleTimeoutMs = 5000;
    private const long IdleGraceMs = 250;
    private const long GlobalCacheBudgetBytes = 1024L * 1024 * 1024;

    private static VideoPlaybackEngine? _instance;
    private readonly Dictionary<Guid, Stream> _streams = new();
}

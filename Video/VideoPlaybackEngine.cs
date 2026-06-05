using System.Collections.Generic;
using T3.Core.Video;

namespace T3.Video;

/// <summary>
/// The global <see cref="IVideoPlaybackEngine"/>. Operators are thin clients: each posts its per-frame
/// request keyed by a stable stream id, and the engine owns the underlying
/// <see cref="VideoPlaybackController"/>s (decode workers + frame caches). Centralizing ownership here is the
/// foundation for the shared cache budget and the bounded decoder pool the timeline clip player needs.
///
/// Accessed only from the operator evaluation thread (graph eval is single-threaded), so the stream map needs
/// no locking.
/// </summary>
public sealed class VideoPlaybackEngine : IVideoPlaybackEngine
{
    /// <summary>The singleton; creating it also publishes it to <see cref="VideoPlayback.Engine"/>.</summary>
    public static VideoPlaybackEngine Instance => _instance ??= Register();

    public VideoFrameResult RequestFrame(Guid streamId, string absolutePath, double requestedSeconds, bool loop, bool renderingToFile)
    {
        if (!_streams.TryGetValue(streamId, out var controller))
        {
            controller = new VideoPlaybackController();
            _streams[streamId] = controller;
            RedistributeBudget();
        }

        var produced = controller.Update(absolutePath, requestedSeconds, loop, renderingToFile);
        return new VideoFrameResult(produced, controller.Texture, controller.Duration, controller.HasCompleted,
                                    controller.IsReady, controller.ErrorMessage);
    }

    public void ReleaseStream(Guid streamId)
    {
        if (!_streams.Remove(streamId, out var controller))
            return;

        controller.Dispose();
        RedistributeBudget();
    }

    // Splits the shared cache budget evenly across live streams: a lone video gets the whole budget; more
    // streams each get a smaller share, keeping the total bounded. (Activity-weighted shares are a later
    // refinement; the bounded pool will cap how many streams are live at once.)
    private void RedistributeBudget()
    {
        var count = _streams.Count;
        if (count == 0)
            return;

        var perStream = GlobalCacheBudgetBytes / count;
        foreach (var controller in _streams.Values)
            controller.SetCacheBudget(perStream);
    }

    private static VideoPlaybackEngine Register()
    {
        var engine = new VideoPlaybackEngine();
        VideoPlayback.Engine = engine;
        return engine;
    }

    private const long GlobalCacheBudgetBytes = 1024L * 1024 * 1024;

    private static VideoPlaybackEngine? _instance;
    private readonly Dictionary<Guid, VideoPlaybackController> _streams = new();
}

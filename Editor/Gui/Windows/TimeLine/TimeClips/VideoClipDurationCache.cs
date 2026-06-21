#nullable enable

using System.Collections.Concurrent;
using System.Threading.Tasks;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Video;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Caches the full source-file duration (seconds) per video asset, probed once via the video assembly's
/// demux-only metadata reader (<see cref="IVideoEncoderFactory.TryProbeDurationSeconds"/>) on a background
/// thread. Lets the timeline outline a clip's available footage (head/tail handles, over-length looping) on
/// hover without waiting for the clip to play. Mirrors <see cref="AudioClipDurationCache"/>.
/// </summary>
internal static class VideoClipDurationCache
{
    /// <summary>
    /// Returns true with the cached duration once known. The first call per asset kicks off an async probe and
    /// returns false; later calls return the result. A failed probe (or no video backend loaded) caches 0 so it
    /// isn't retried every frame.
    /// </summary>
    public static bool TryGetDurationSecs(string assetPath, IResourceConsumer owner, out double durationSecs)
    {
        if (_durationByAssetPath.TryGetValue(assetPath, out durationSecs))
            return durationSecs > 0;

        // First request for this asset: resolve the absolute path (cheap, on this thread) and probe the
        // duration on a worker so the draw thread never blocks on an FFmpeg file open.
        if (_probing.TryAdd(assetPath, true))
        {
            var factory = VideoExport.Factory;
            if (factory != null && AssetRegistry.TryResolveAddress(assetPath, owner, out var absolutePath, out _))
            {
                var path = absolutePath;
                var key = assetPath;
                Task.Run(() =>
                         {
                             _durationByAssetPath[key] = factory.TryProbeDurationSeconds(path, out var secs) ? secs : 0;
                             _probing.TryRemove(key, out _);
                         });
            }
            else
            {
                _durationByAssetPath[assetPath] = 0;
                _probing.TryRemove(assetPath, out _);
            }
        }

        durationSecs = 0;
        return false;
    }

    private static readonly ConcurrentDictionary<string, double> _durationByAssetPath = new();
    private static readonly ConcurrentDictionary<string, bool> _probing = new();
}

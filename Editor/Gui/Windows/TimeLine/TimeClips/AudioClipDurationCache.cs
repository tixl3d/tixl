#nullable enable

using System.Collections.Concurrent;
using System.Threading.Tasks;
using T3.Core.Audio;
using T3.Core.Resource;
using T3.Core.Resource.Assets;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Caches the full source-file duration (seconds) per audio asset, probed once via BASS on a
/// background thread. Lets the timeline waveform map its source window immediately — without
/// waiting for the clip to play once (which is the only other place
/// <c>TimelineAudioClip.LengthInSeconds</c> gets populated).
/// </summary>
internal static class AudioClipDurationCache
{
    /// <summary>
    /// Returns true with the cached duration once known. The first call per asset kicks off an
    /// async probe and returns false; later calls return the result. A failed probe caches 0 so it
    /// isn't retried every frame.
    /// </summary>
    public static bool TryGetDurationSecs(string assetPath, IResourceConsumer owner, out double durationSecs)
    {
        if (_durationByAssetPath.TryGetValue(assetPath, out durationSecs))
            return durationSecs > 0;

        // First request for this asset: resolve the absolute path (cheap, on this thread) and probe
        // the duration on a worker so the draw thread never blocks on a BASS file open.
        if (_probing.TryAdd(assetPath, true))
        {
            if (AssetRegistry.TryResolveAddress(assetPath, owner, out var absolutePath, out _))
            {
                var path = absolutePath;
                var key = assetPath;
                Task.Run(() =>
                         {
                             _durationByAssetPath[key] = AudioMixerManager.TryProbeAudioDurationSecs(path);
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

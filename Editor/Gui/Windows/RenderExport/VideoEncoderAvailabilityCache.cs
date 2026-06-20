#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Video;

namespace T3.Editor.Gui.Windows.RenderExport;

/// <summary>
/// Resolves — off the UI thread — how the FFmpeg encoder will serve each <see cref="VideoExportCodec"/> on this
/// machine (hardware vs. in-process software), so the render window can show an inline indicator without stalling a
/// draw frame on the one-shot hardware-encoder probe (a GPU-encoder open). Results are cached for the session.
/// </summary>
internal static class VideoEncoderAvailabilityCache
{
    /// <summary>The cached availability for <paramref name="codec"/>, or null while it is still being probed.</summary>
    public static VideoEncoderAvailability? Get(VideoExportCodec codec)
    {
        if (_results.TryGetValue(codec, out var result))
            return result;

        // First request for this codec: kick the probe onto a background thread exactly once.
        if (_pending.TryAdd(codec, true))
            Task.Run(() => Probe(codec));

        return null;
    }

    /// <summary>Resolves availability synchronously, using the cache. For the export decision (off the UI thread,
    /// where a one-time blocking probe is acceptable); the UI uses <see cref="Get"/> to avoid stalling a frame.</summary>
    public static VideoEncoderAvailability GetBlocking(VideoExportCodec codec)
    {
        if (_results.TryGetValue(codec, out var result))
            return result;

        var resolved = Resolve(codec);
        _results[codec] = resolved;
        return resolved;
    }

    private static void Probe(VideoExportCodec codec)
    {
        _results[codec] = Resolve(codec);
        _pending.TryRemove(codec, out _);
    }

    private static VideoEncoderAvailability Resolve(VideoExportCodec codec)
    {
        try
        {
            var factory = VideoExport.Factory;
            if (factory == null)
            {
                // Same eager-registration nudge the export path uses: the Video package's [ModuleInitializer]
                // may not have run yet in a project that never touched a video operator.
                foreach (var package in SymbolPackage.AllPackages)
                    package.AssemblyInformation.RunModuleInitializers();
                factory = VideoExport.Factory;
            }

            return factory?.GetAvailability(codec) ?? new VideoEncoderAvailability { Kind = VideoEncoderKind.Unavailable };
        }
        catch (Exception e)
        {
            Log.Warning("Failed to probe video encoder availability: " + e.Message);
            return new VideoEncoderAvailability { Kind = VideoEncoderKind.Unavailable };
        }
    }

    private static readonly ConcurrentDictionary<VideoExportCodec, VideoEncoderAvailability> _results = new();
    private static readonly ConcurrentDictionary<VideoExportCodec, bool> _pending = new();
}

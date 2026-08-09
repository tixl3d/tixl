#nullable enable

using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDX.Direct3D11;
using SharpDX.WIC;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.Settings;
using T3.Core.Video;
using T3.Editor.Gui.UiHelpers.Thumbnails;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Provides small video-frame thumbnails for timeline clips at a clip's source in/out points, cached as PNGs
/// under the Tmp thumbnails folder (keyed by asset path + write time + frame time) and drawn from the shared
/// <see cref="ThumbnailManager"/> atlas.
///
/// Deliberately limited to the in/out points: a preview that follows the mouse continuously cannot be keyed by
/// time without filling the atlas with near-duplicates nothing ever looks at twice. Skimming needs its own
/// dedicated texture, not this cache.
///
/// All decoding runs on one low-priority worker that owns a single <see cref="IVideoThumbnailReader"/> —
/// completely separate from the playback engine's frame caches, so thumbnails never evict playback frames.
/// </summary>
internal static class VideoClipThumbnailCache
{
    /// <summary>
    /// Returns true with the atlas rect once the thumbnail for <paramref name="sourceSeconds"/> is ready.
    /// Otherwise returns false and — when <paramref name="allowRequest"/> — queues generation. Callers quantize
    /// <paramref name="sourceSeconds"/> themselves; the millisecond-rounded time is the cache key.
    /// </summary>
    public static bool TryGetThumbnail(string assetPath, IResourceConsumer owner, double sourceSeconds,
                                       bool allowRequest, out ThumbnailManager.ThumbnailRect rect)
    {
        rect = default;

        if (!TryGetPathInfo(assetPath, owner, allowRequest, out var pathInfo))
            return false;

        var timeMs = (long)Math.Round(Math.Max(0, sourceSeconds) * 1000);
        var key = new ThumbKey(assetPath, timeMs);
        if (!_entries.TryGetValue(key, out var entry))
            entry = CreateEntry(key, pathInfo, timeMs);

        switch (entry.State)
        {
            case EntryState.Failed:
                return false;

            case EntryState.Pushed:
                switch (ThumbnailManager.GetPushedThumbnail(entry.ThumbGuid, out rect))
                {
                    case ThumbnailManager.PushedState.Ready:
                        return true;
                    case ThumbnailManager.PushedState.Pending:
                        return false;
                    default:
                        // Evicted from the atlas — regenerate (reloads from the PNG).
                        entry.State = EntryState.New;
                        break;
                }

                break;
        }

        if (entry.State == EntryState.New && allowRequest)
        {
            entry.State = EntryState.Queued;
            EnqueueRequest(new Request(entry, pathInfo.AbsolutePath!, sourceSeconds));
        }

        return false;
    }

    #region Request queue and worker
    private static void EnqueueRequest(in Request request)
    {
        EnsureWorkerRunning();
        _requestQueue.Enqueue(request);
        _workSignal.Release();
    }

    private static void EnsureWorkerRunning()
    {
        if (_worker != null)
            return;

        lock (_workerLock)
        {
            _worker ??= Task.Factory.StartNew(WorkerLoop, CancellationToken.None,
                                              TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    private static void WorkerLoop()
    {
        while (true)
        {
            // Idle timeout: release the decoder session (and its file handle) when no requests arrive for a while.
            if (!_workSignal.Wait(2000))
            {
                if (_requestQueue.IsEmpty)
                {
                    _reader?.Dispose();
                    _reader = null;
                    _readerPath = null;
                }

                continue;
            }

            while (_requestQueue.TryDequeue(out var request))
                ProcessRequest(request);
        }
        // ReSharper disable once FunctionNeverReturns — lives for the editor session.
    }

    private static void ProcessRequest(in Request request)
    {
        var entry = request.Entry;
        try
        {
            var pngPath = GetPngPath(entry.ThumbGuid);
            if (File.Exists(pngPath))
            {
                var loaded = ThumbnailManager.LoadTextureViaWic(pngPath).GetAwaiter().GetResult();
                if (loaded != null)
                {
                    ThumbnailManager.PushSlotTexture(entry.ThumbGuid, loaded);
                    entry.State = EntryState.Pushed;
                    return;
                }
            }

            if (!EnsureReader(request.AbsolutePath)
                || !_reader!.TryReadFrame(request.Seconds, SlotWidth, SlotHeight, _rgbaBuffer))
            {
                entry.State = EntryState.Failed;
                return;
            }

            TryWritePng(pngPath, _rgbaBuffer);

            ThumbnailManager.PushSlotTexture(entry.ThumbGuid, CreateSlotTexture(_rgbaBuffer));
            entry.State = EntryState.Pushed;
        }
        catch (Exception e)
        {
            Log.Warning($"Video thumbnail generation failed: {e.Message}");
            entry.State = EntryState.Failed;
        }
    }

    private static bool EnsureReader(string absolutePath)
    {
        if (_reader != null && _readerPath == absolutePath)
            return true;

        _reader?.Dispose();
        _reader = null;
        _readerPath = null;

        var factory = VideoExport.Factory;
        if (factory == null)
            return false;

        // A generated proxy is all-intra and small — much cheaper to seek than a long-GOP source.
        var proxyPath = VideoPlayback.GetProxyPath(absolutePath);
        var decodePath = File.Exists(proxyPath) ? proxyPath : absolutePath;

        _reader = factory.TryCreateThumbnailReader(decodePath, out _);
        if (_reader == null)
            return false;

        _readerPath = absolutePath;
        return true;
    }
    #endregion

    #region Texture and PNG helpers
    private static SharpDX.Direct3D11.Texture2D CreateSlotTexture(byte[] rgba)
    {
        var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            return new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, new Texture2DDescription
                                                        {
                                                            Width = SlotWidth,
                                                            Height = SlotHeight,
                                                            ArraySize = 1,
                                                            BindFlags = BindFlags.ShaderResource,
                                                            Usage = ResourceUsage.Immutable,
                                                            Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                                                            MipLevels = 1,
                                                            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                                                        },
                                                    new SharpDX.DataRectangle(handle.AddrOfPinnedObject(), SlotWidth * 4));
        }
        finally
        {
            handle.Free();
        }
    }

    private static void TryWritePng(string path, byte[] rgba)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var factory = new ImagingFactory();
            using var stream = new WICStream(factory, path, SharpDX.IO.NativeFileAccess.Write);
            using var encoder = new PngBitmapEncoder(factory, stream);
            using var frame = new BitmapFrameEncode(encoder);
            frame.Initialize();
            frame.SetSize(SlotWidth, SlotHeight);

            // The PNG encoder has no native RGBA format: asking for one makes WIC silently substitute BGRA and
            // label our bytes with the wrong channel order, so red and blue come back swapped on reload.
            var pixelFormat = PixelFormat.Format32bppBGRA;
            frame.SetPixelFormat(ref pixelFormat);

            for (var i = 0; i < rgba.Length; i += 4)
            {
                _bgraBuffer[i + 0] = rgba[i + 2];
                _bgraBuffer[i + 1] = rgba[i + 1];
                _bgraBuffer[i + 2] = rgba[i + 0];
                _bgraBuffer[i + 3] = rgba[i + 3];
            }

            var handle = GCHandle.Alloc(_bgraBuffer, GCHandleType.Pinned);
            try
            {
                frame.WritePixels(SlotHeight, new SharpDX.DataRectangle(handle.AddrOfPinnedObject(), SlotWidth * 4));
            }
            finally
            {
                handle.Free();
            }

            frame.Commit();
            encoder.Commit();
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to save video thumbnail {path}: {e.Message}");
        }
    }

    private static string GetPngPath(Guid thumbGuid)
        => Path.Combine(FileLocations.TempFolder, ThumbnailManager.ThumbnailsSubFolder, "VideoClips", $"{thumbGuid}.png");
    #endregion

    #region Path resolution and keying
    /// <summary>Resolves the asset once per session; false while the resolve is still pending or it failed.</summary>
    private static bool TryGetPathInfo(string assetPath, IResourceConsumer owner, bool allowRequest, out PathInfo pathInfo)
    {
        if (!_pathInfos.TryGetValue(assetPath, out pathInfo))
        {
            if (allowRequest)
                StartResolvingPathInfo(assetPath, owner);
            return false;
        }

        return pathInfo.AbsolutePath != null;
    }

    private static void StartResolvingPathInfo(string assetPath, IResourceConsumer owner)
    {
        if (!_resolving.TryAdd(assetPath, true))
            return;

        if (!AssetRegistry.TryResolveAddress(assetPath, owner, out var absolutePath, out _))
        {
            _pathInfos[assetPath] = new PathInfo(null, 0);
            _resolving.TryRemove(assetPath, out _);
            return;
        }

        var key = assetPath;
        var path = absolutePath;
        Task.Run(() =>
                 {
                     // The write time folds into the thumbnail key so re-rendered files don't show stale frames.
                     long writeTicks = 0;
                     try
                     {
                         writeTicks = File.GetLastWriteTimeUtc(path).Ticks;
                     }
                     catch
                     {
                         // Missing file: decode will fail and cache the failure.
                     }

                     _pathInfos[key] = new PathInfo(path, writeTicks);
                     _resolving.TryRemove(key, out _);
                 });
    }

    private static Entry CreateEntry(in ThumbKey key, in PathInfo pathInfo, long timeMs)
    {
        if (ThumbnailManager.EnableLogging)
            Log.Debug($"VideoThumb key: t={timeMs}ms {key.AssetPath}");

        var entry = new Entry { ThumbGuid = DeriveGuid(pathInfo, timeMs) };
        _entries[key] = entry;
        return entry;
    }

    private static Guid DeriveGuid(in PathInfo pathInfo, long timeMs)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{CacheVersion}|{pathInfo.AbsolutePath}|{pathInfo.WriteTimeTicks}|{timeMs}"));
        return new Guid(hash);
    }
    #endregion

    // Bump to discard on-disk thumbnails written by an earlier, incompatible encoding.
    private const int CacheVersion = 2;

    private const int SlotWidth = ThumbnailManager.SlotWidth;
    private const int SlotHeight = ThumbnailManager.SlotHeight;

    private enum EntryState { New, Queued, Pushed, Failed }

    private sealed class Entry
    {
        public Guid ThumbGuid;
        public volatile EntryState State = EntryState.New;
    }

    private readonly record struct ThumbKey(string AssetPath, long TimeMs);
    private readonly record struct PathInfo(string? AbsolutePath, long WriteTimeTicks);
    private readonly record struct Request(Entry Entry, string AbsolutePath, double Seconds);

    // _entries is draw-thread-only; the worker touches entries only through the volatile State field.
    private static readonly Dictionary<ThumbKey, Entry> _entries = new();
    private static readonly ConcurrentDictionary<string, PathInfo> _pathInfos = new();
    private static readonly ConcurrentDictionary<string, bool> _resolving = new();

    private static readonly ConcurrentQueue<Request> _requestQueue = new();
    private static readonly object _workerLock = new();
    private static readonly SemaphoreSlim _workSignal = new(0);
    private static Task? _worker;

    // Worker-owned decode state.
    private static IVideoThumbnailReader? _reader;
    private static string? _readerPath;
    private static readonly byte[] _rgbaBuffer = new byte[SlotWidth * SlotHeight * 4];
    private static readonly byte[] _bgraBuffer = new byte[SlotWidth * SlotHeight * 4];
}

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
/// Provides small video-frame thumbnails for timeline clips: persistent ones at a clip's source in/out points
/// (cached as PNGs under the Tmp thumbnails folder, keyed by asset path + write time + frame time) and
/// session-only ones for the position under the mouse while hovering.
///
/// All decoding runs on one low-priority worker that owns a single <see cref="IVideoThumbnailReader"/> —
/// completely separate from the playback engine's frame caches, so thumbnails never evict playback frames.
/// Hover requests are latest-wins (scrubbing along a clip only ever keeps one decode in flight), and results
/// land in the shared <see cref="ThumbnailManager"/> atlas.
/// </summary>
internal static class VideoClipThumbnailCache
{
    /// <summary>
    /// Returns true with the atlas rect once the thumbnail for <paramref name="sourceSeconds"/> is ready.
    /// Otherwise returns false and — when <paramref name="allowRequest"/> — queues generation. Callers quantize
    /// <paramref name="sourceSeconds"/> themselves; the millisecond-rounded time is the cache key.
    /// </summary>
    public static bool TryGetThumbnail(string assetPath, IResourceConsumer owner, double sourceSeconds,
                                       bool persistent, bool allowRequest, out ThumbnailManager.ThumbnailRect rect)
    {
        rect = default;

        if (!_pathInfos.TryGetValue(assetPath, out var pathInfo))
        {
            if (allowRequest)
                StartResolvingPathInfo(assetPath, owner);
            return false;
        }

        if (pathInfo.AbsolutePath == null)
            return false;

        var timeMs = (long)Math.Round(Math.Max(0, sourceSeconds) * 1000);
        var key = new ThumbKey(assetPath, timeMs);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry { ThumbGuid = DeriveGuid(pathInfo, timeMs) };
            _entries[key] = entry;
        }

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
                        // Evicted from the atlas — regenerate (persistent ones reload from their PNG).
                        entry.State = EntryState.New;
                        break;
                }

                break;
        }

        if (entry.State == EntryState.New && allowRequest)
        {
            entry.State = EntryState.Queued;
            EnqueueRequest(new Request(entry, pathInfo.AbsolutePath, sourceSeconds, persistent));
        }

        return false;
    }

    #region Request queue and worker
    private static void EnqueueRequest(in Request request)
    {
        EnsureWorkerRunning();

        if (request.Persistent)
        {
            _persistentQueue.Enqueue(request);
        }
        else
        {
            lock (_hoverLock)
            {
                // Latest-wins: a superseded hover request is dropped, so its entry must become requestable again.
                if (_pendingHover is { } dropped && dropped.Entry.State == EntryState.Queued)
                    dropped.Entry.State = EntryState.New;
                _pendingHover = request;
            }
        }

        _workSignal.Release();
    }

    private static void EnsureWorkerRunning()
    {
        if (_worker != null)
            return;

        lock (_hoverLock)
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
                if (_pendingHover == null && _persistentQueue.IsEmpty)
                {
                    _reader?.Dispose();
                    _reader = null;
                    _readerPath = null;
                }

                continue;
            }

            while (TryTakeNextRequest(out var request))
                ProcessRequest(request);
        }
        // ReSharper disable once FunctionNeverReturns — lives for the editor session.
    }

    private static bool TryTakeNextRequest(out Request request)
    {
        // Hover first: it's what the user is looking at right now.
        lock (_hoverLock)
        {
            if (_pendingHover is { } hover)
            {
                _pendingHover = null;
                request = hover;
                return true;
            }
        }

        return _persistentQueue.TryDequeue(out request);
    }

    private static void ProcessRequest(in Request request)
    {
        var entry = request.Entry;
        try
        {
            var pngPath = request.Persistent ? GetPngPath(entry.ThumbGuid) : null;

            if (pngPath != null && File.Exists(pngPath))
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

            if (pngPath != null)
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
            var pixelFormat = PixelFormat.Format32bppRGBA;
            frame.SetPixelFormat(ref pixelFormat);

            var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
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

    private static Guid DeriveGuid(in PathInfo pathInfo, long timeMs)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{pathInfo.AbsolutePath}|{pathInfo.WriteTimeTicks}|{timeMs}"));
        return new Guid(hash);
    }
    #endregion

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
    private readonly record struct Request(Entry Entry, string AbsolutePath, double Seconds, bool Persistent);

    // _entries is draw-thread-only; the worker touches entries only through the volatile State field.
    private static readonly Dictionary<ThumbKey, Entry> _entries = new();
    private static readonly ConcurrentDictionary<string, PathInfo> _pathInfos = new();
    private static readonly ConcurrentDictionary<string, bool> _resolving = new();

    private static readonly ConcurrentQueue<Request> _persistentQueue = new();
    private static readonly object _hoverLock = new();
    private static Request? _pendingHover;
    private static readonly SemaphoreSlim _workSignal = new(0);
    private static Task? _worker;

    // Worker-owned decode state.
    private static IVideoThumbnailReader? _reader;
    private static string? _readerPath;
    private static readonly byte[] _rgbaBuffer = new byte[SlotWidth * SlotHeight * 4];
}

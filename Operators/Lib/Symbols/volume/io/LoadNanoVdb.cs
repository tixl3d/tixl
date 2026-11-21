using System;
using System.Numerics;
using System.Threading;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;
using Lib.Utils;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Graphics.Compat;

namespace Lib.volume.io;

/// Loads a NanoVDB .nvdb file and uploads it as a structured uint buffer, ready for [NanoVdbRender] or a custom HLSL traversal. Unlike [LoadVdb] this skips the CPU grid entirely, so a pre-converted cache plays without parsing or rebuilding anything. Outputs mirror [VolumeToNanoVdb], and WorldMin/Max frame the grid.
[Guid("fc358fd5-f6ba-4971-9d40-2090e51aaa71")]
internal sealed class LoadNanoVdb : Instance<LoadNanoVdb>
{
    [Output(Guid = "4e4faeca-b648-4704-98b8-f0785bf6953d")]
    public readonly Slot<BufferWithViews> Buffer = new();

    [Output(Guid = "fb21f061-79e7-486c-b08c-dcce202daf34")]
    public readonly Slot<ShaderResourceView> ShaderResourceView = new();

    [Output(Guid = "0b6b0f47-6272-4f4e-ad92-a90bf1d9c911")]
    public readonly Slot<Vector4> TreeOffsets = new();

    [Output(Guid = "676f0880-d184-40ad-9df9-f05c49c06b20")]
    public readonly Slot<float> VoxelCount = new();

    [Output(Guid = "ff56e7b9-b7de-4d27-b025-5a70033803d8")]
    public readonly Slot<float> VoxelSize = new();

    [Output(Guid = "8ec10b96-380d-4043-8a15-b4dec1b3daf7")]
    public readonly Slot<string> GridName = new();

    [Output(Guid = "cd870b7c-8aa0-4761-8273-9c0d1ca36008")]
    public readonly Slot<Vector3> WorldMin = new();

    [Output(Guid = "30f4d753-17da-4610-b704-6cceaf334407")]
    public readonly Slot<Vector3> WorldMax = new();

    public LoadNanoVdb()
    {
        // Any output can be the one a consumer pulls, so each must trigger Update.
        Buffer.UpdateAction = Update;
        ShaderResourceView.UpdateAction = Update;
        TreeOffsets.UpdateAction = Update;
        VoxelCount.UpdateAction = Update;
        VoxelSize.UpdateAction = Update;
        GridName.UpdateAction = Update;
        WorldMin.UpdateAction = Update;
        WorldMax.UpdateAction = Update;
        _polledOutputs = [Buffer, ShaderResourceView, TreeOffsets, VoxelCount, VoxelSize, GridName, WorldMin, WorldMax];
    }

    private void Update(EvaluationContext context)
    {
        UpdateCore(context);
        BackgroundPolling.Set(_loadTask != null, _polledOutputs);
    }

    private readonly ISlot[] _polledOutputs;

    private void UpdateCore(EvaluationContext context)
    {
        var address = Path.GetValue(context)?.Trim();

        if (string.IsNullOrEmpty(address))
        {
            CancelPending();
            ClearOutputs();
            _lastPath = null;
            return;
        }

        // A pattern ("cache_####.nvdb") addresses an animated cache; a plain path is a still frame.
        var frame = Frame.GetValue(context);
        var path = ResolvePath(VdbSequence.Expand(address, frame));

        if (path == null)
        {
            CancelPending();
            ClearOutputs();
            _lastPath = null;
            return;
        }

        var lastWrite = File.GetLastWriteTimeUtc(path);
        if (path != _lastPath || lastWrite != _lastWriteTime)
        {
            // Remembered before loading so a broken file schedules once instead of on every evaluation.
            _lastPath = path;
            _lastWriteTime = lastWrite;

            // The first load for a path publishes inline: TiXL's dirty tracking is pull-based and a
            // consumer that cached the empty value during the first evaluation never re-pulls, so a
            // grid that arrived from the worker would never reach it. Inline keeps the one-time
            // cost on the frame that changed the input (like [VolumeToNanoVdb]); large caches and
            // animated sequences still go through the worker below.
            var fileSize = new FileInfo(path).Length;
            if (fileSize <= InlineLoadMaxBytes)
            {
                PublishResult(LoadFileChecked(path));
            }
            else
            {
                StartLoad(path);
            }
        }

        PrefetchFollowingFrames(address, frame, PreloadFrames.GetValue(context));
        PublishCompletedLoad();
    }

    /// <summary>Resolves a disk path or a package address to an existing file, or null.</summary>
    private string ResolvePath(string address)
    {
        if (string.IsNullOrEmpty(address))
            return null;

        if (File.Exists(address))
            return address;

        // Windows drive paths are plain disk paths, never package addresses.
        if (address.Length >= 2 && char.IsLetter(address[0]) && address[1] == ':')
            return null;

        return FileResource.TryGetFileResource(address, this, out var fileResource) && File.Exists(fileResource.AbsolutePath)
                   ? fileResource.AbsolutePath
                   : null;
    }

    /// <summary>
    /// Reads a few upcoming frames into the shared byte cache while the current one is displayed.
    /// One task at a time, and a frame too large for the cache is skipped rather than read and
    /// immediately evicted.
    /// </summary>
    private void PrefetchFollowingFrames(string address, int frame, int ahead)
    {
        if (_prefetchTask is { IsCompleted: false } || ahead <= 0 || !VdbSequence.IsAnimated(address))
            return;

        // Steady state re-evaluates the same displayed frame over and over; its followers are
        // already in the cache, so schedule once per frame number instead of once per evaluation.
        if (frame == _lastPrefetchFrame)
            return;
        _lastPrefetchFrame = frame;

        var budget = FrameCache.BudgetBytes;
        var upcoming = new List<string>();
        for (var offset = 1; offset <= Math.Clamp(ahead, 0, 32); offset++)
        {
            var candidate = ResolvePath(VdbSequence.Expand(address, frame + offset));
            if (candidate != null)
                upcoming.Add(candidate);
        }

        if (upcoming.Count == 0)
            return;

        _prefetchTask = Task.Run(() =>
                                     {
                                         foreach (var next in upcoming)
                                         {
                                             if (FrameCache.TryTake(next, out _))
                                                 continue;

                                             try
                                             {
                                                 var length = new FileInfo(next).Length;
                                                 if (length > budget / 2)
                                                     continue;

                                                 FrameCache.Put(next, File.ReadAllBytes(next));
                                             }
                                             catch
                                             {
                                                 // Prefetching is an optimisation; a missing or locked frame
                                                 // is picked up by the synchronous path when it is shown.
                                             }
                                         }
                                     });
    }

    /// <summary>
    /// A production cache can be hundreds of megabytes, so the read happens on a worker thread and
    /// only the finished grid is published here, on the evaluation thread. GPU resources are never
    /// created off the evaluation thread: fine on D3D11, not guaranteed on Vulkan.
    /// </summary>
    private void StartLoad(string path)
    {
        CancelPending();

        _loadTask = Task.Run(() =>
                                 {
                                     var result = LoadFileChecked(path);

                                     // Wake the graph: the slot itself is clean, so without this the
                                     // finished grid would only be published if something else
                                     // invalidated the chain. Bump the version atomically rather than
                                     // calling Invalidate(), which also rewrites the pass-scoped tick
                                     // owned by the evaluation thread.
                                     Interlocked.Increment(ref Buffer.DirtyFlag.SourceVersion);
                                     return result;
                                 });
    }

    /// <summary>Reads and validates one file. Runs on the worker for large files, inline for small ones.</summary>
    private LoadResult LoadFileChecked(string path)
    {
        try
        {
            // A frame the preloader already read is used as-is; only a miss touches the disk, and
            // then without the extra copy LoadBytes makes.
            if (FrameCache.TryTake(path, out var cached))
            {
                var cachedGrid = NanoVdbBuffer.LoadBytes(cached!);
                return cachedGrid.TryValidate(out var cachedInvalid)
                           ? new LoadResult { Grid = cachedGrid, Path = path }
                           : new LoadResult { Path = path, Error = cachedInvalid };
            }

            var length = new FileInfo(path).Length;
            if (length > MaxFileBytes)
            {
                return new LoadResult
                           {
                               Path = path,
                               Error = $"file is {length / 1024 / 1024} MB, above the {MaxFileBytes / 1024 / 1024} MB limit for the GPU path",
                           };
            }

            var grid = NanoVdbBuffer.LoadFile(path);
            return grid.TryValidate(out var invalid)
                       ? new LoadResult { Grid = grid, Path = path }
                       : new LoadResult { Path = path, Error = invalid };
        }
        catch (Exception e)
        {
            return new LoadResult { Path = path, Error = e.Message };
        }
    }

    private void PublishCompletedLoad()
    {
        if (_loadTask is not { IsCompleted: true })
            return;

        var task = _loadTask;
        _loadTask = null;

        PublishResult(task.Result);
    }

    /// <summary>Uploads a finished load on the evaluation thread, where GPU resources are safe to create.</summary>
    private void PublishResult(LoadResult result)
    {
        if (result.Error != null)
        {
            Log.Warning($"LoadNanoVdb failed for {result.Path}: {result.Error}", this);
            ClearOutputs();
            return;
        }

        var grid = result.Grid;
        if (grid == null)
        {
            ClearOutputs();
            return;
        }

        try
        {
            var info = grid.ReadInfo();

            var byteCount = (grid.Buffer.Length + 3) / 4 * 4;
            using var data = new DataStream(byteCount, true, true);
            data.Write(grid.Buffer, 0, grid.Buffer.Length);
            data.Position = 0;

            // Reuse one BufferWithViews so the SRV is only recreated when the buffer is replaced.
            ResourceManager.SetupStructuredBuffer(data, byteCount, sizeof(uint), ref _bufferWithViews.Buffer);
            ResourceManager.CreateStructuredBufferSrv(_bufferWithViews.Buffer, ref _bufferWithViews.Srv);

            var treeStart = NanoVdbBuffer.GridDataSize;
            Buffer.Value = _bufferWithViews;
            ShaderResourceView.Value = _bufferWithViews.Srv;
            TreeOffsets.Value = new Vector4(BitConverter.ToUInt64(grid.Buffer, treeStart),
                                            BitConverter.ToUInt64(grid.Buffer, treeStart + 8),
                                            BitConverter.ToUInt64(grid.Buffer, treeStart + 16),
                                            BitConverter.ToUInt64(grid.Buffer, treeStart + 24));
            VoxelCount.Value = info.VoxelCount;
            VoxelSize.Value = info.VoxelSize;
            GridName.Value = info.Name;
            WorldMin.Value = info.WorldMin;
            WorldMax.Value = info.WorldMax;
        }
        catch (Exception e)
        {
            Log.Warning($"LoadNanoVdb failed to upload {result.Path}: {e.Message}", this);
            ClearOutputs();
        }
    }

    private void ClearOutputs()
    {
        Buffer.Value = null;
        ShaderResourceView.Value = null;
        TreeOffsets.Value = Vector4.Zero;
        VoxelCount.Value = 0;
        VoxelSize.Value = 0;
        GridName.Value = string.Empty;
        WorldMin.Value = Vector3.Zero;
        WorldMax.Value = Vector3.Zero;
    }

    private void CancelPending()
    {
        // A running read cannot be aborted mid-parse; dropping the task discards its result.
        _loadTask = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _bufferWithViews.Dispose();

        base.Dispose(disposing);
    }

    private sealed class LoadResult
    {
        public NanoVdbBuffer Grid;
        public string Error;
        public string Path = string.Empty;
    }

    /// <summary>Windows drive paths are plain disk paths, never package addresses.</summary>
    private static bool IsDrivePath(string path)
        => path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';

    private const long MaxFileBytes = 1L << 30;

    /// <summary>Files up to this size load inline on the evaluation thread instead of through the
    /// worker: a one-frame hitch is preferable to a late publish that a settled graph never sees.</summary>
    private const long InlineLoadMaxBytes = 24L << 20;

    /// <summary>
    /// Shared by every LoadNanoVdb in the process, so two nodes reading density and velocity from
    /// the same sequence share their preloaded frames. Animated caches are 50-500 MB per frame, so
    /// the budget is in bytes and a frame that cannot fit is not retained.
    /// </summary>
    private static readonly VdbFrameCache FrameCache = new(512L * 1024 * 1024);

    private string _lastPath;
    private DateTime _lastWriteTime;
    private Task<LoadResult> _loadTask;
    private Task _prefetchTask;
    private int _lastPrefetchFrame = -1;
    private readonly BufferWithViews _bufferWithViews = new();

    [Input(Guid = "69a0c6d6-243c-4cf2-b022-c557955660fd")]
    public readonly InputSlot<string> Path = new();

    [Input(Guid = "1f4c2a80-5d3e-4b97-8a61-2e7d9c0b4f13")]
    public readonly InputSlot<int> Frame = new();

    [Input(Guid = "7b2e9d14-3c8a-4f60-9d52-6a0e1c7b8f25")]
    public readonly InputSlot<int> PreloadFrames = new();
}

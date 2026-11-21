using System;
using System.Collections.Generic;
using System.Threading;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;
using Lib.Utils;
using T3.Core.Operator.Slots;
using T3.Graphics.Compat;

namespace Lib.volume.convert;

/// Bakes a volume into a dense 3D texture (R32Float) plus its world-space bounds - the GPU bridge for raymarching. Connect the texture to [VolumeField] and feed that into [RaymarchField].
[Guid("db930e54-f0aa-5894-88ae-67b54353c513")]
internal sealed class VolumeToTexture3D : Instance<VolumeToTexture3D>
{
    [Output(Guid = "18e3a08c-edc3-5c42-a11f-eccaee753caa")]
    public readonly Slot<Texture3dWithViews> Texture = new();

    [Output(Guid = "1449308e-6d89-52eb-9ce2-124c4a972dab")]
    public readonly Slot<Vector3> BoundsMin = new();

    [Output(Guid = "ca612cd0-3d23-59a1-912e-cad23f75d645")]
    public readonly Slot<Vector3> BoundsMax = new();


    public VolumeToTexture3D()
    {
        // Any output can be the one a consumer pulls, so each must trigger Update.
        Texture.UpdateAction = Update;
        BoundsMin.UpdateAction = Update;
        BoundsMax.UpdateAction = Update;
        _polledOutputs = [Texture, BoundsMin, BoundsMax];
    }

    private void Update(EvaluationContext context)
    {
        UpdateCore(context);
        BackgroundPolling.Set(_extractTask != null, _polledOutputs);
    }

    private readonly ISlot[] _polledOutputs;

    private void UpdateCore(EvaluationContext context)
    {
        var volume = Volume.GetValue(context);
        var maxResolution = Math.Clamp((int)MaxResolution.GetValue(context), 8, 2048);

        var grid = volume?.GetFloatGrid();
        if (grid == null)
        {
            _extractTask = null;
            _pendingKey = default;
            ReleaseTexture();
            ClearOutputs();
            _last = default;
            return;
        }

        var key = (volume, maxResolution);
        if (key == _last && _extractTask == null)
            return;

        // Extracting a dense volume is serial and can touch tens of millions of voxels. The bake runs on
        // a worker; the eval thread only uploads a finished result and otherwise keeps the previous texture.
        if (_extractTask != null && _extractTask.IsCompleted)
        {
            var task = _extractTask;
            var completedKey = _pendingKey;
            var completedGrid = _pendingGrid;
            _extractTask = null;
            _pendingKey = default;
            _pendingGrid = null;

            DenseVolume dense;
            try
            {
                dense = task.Result;
            }
            catch (Exception e)
            {
                if (e.InnerException?.Message != _lastError)
                    Log.Warning($"VolumeToTexture3D failed: {e.InnerException?.Message ?? e.Message}", this);
                _lastError = e.InnerException?.Message ?? e.Message;
                _failedKey = key;
                ReleaseTexture();
                ClearOutputs();
                _last = default;
                return;
            }

            // A volume that changes every frame (animated noise) makes completedKey != key on almost
            // every bake. Uploading the finished bake anyway keeps the texture alive and one frame
            // behind, instead of discarding every bake and never publishing a texture at all.
            if (dense != null && completedGrid != null)
            {
                if (TryUpload(dense, completedGrid))
                {
                    _last = completedKey;
                    _failedKey = default;
                    _lastError = null;

                    // The bake finished on a worker; wake the consumers that read these slots.
                    Texture.DirtyFlag.Invalidate();
                    BoundsMin.DirtyFlag.Invalidate();
                    BoundsMax.DirtyFlag.Invalidate();
                }
                else
                {
                    // Do not retry a deterministic upload failure every frame.
                    _failedKey = completedKey;
                }
            }
            else
            {
                ReleaseTexture();
                ClearOutputs();
                _last = default;
            }

            // The input moved on while that bake ran; start the current volume now rather than
            // waiting for another evaluation that a clean static graph may never make.
            if (key != _last && key != _failedKey && _extractTask == null)
            {
                _pendingKey = key;
                _pendingGrid = grid;
                _extractTask = Task.Run(() => VolumeDense.ExtractDense(grid, maxResolution));
            }

            return;
        }

        if (_extractTask != null)
            return;

        if (key == _failedKey)
            return;

        // A first bake for a key publishes inline when the dense box is small: TiXL's dirty tracking
        // is pull-based and a consumer that cached the empty texture during the first evaluation
        // never re-pulls, so a bake that arrived from the worker would never reach it. Inline keeps
        // the one-time cost on the frame that changed the input (like [VolumeToNanoVdb]); larger
        // resolutions still go through the worker below.
        if (maxResolution <= InlineBakeMaxResolution)
        {
            var denseInline = VolumeDense.ExtractDense(grid, maxResolution);
            if (denseInline == null)
            {
                // An empty grid is a valid empty output, not a failed bake.
                ReleaseTexture();
                ClearOutputs();
                _last = key;
                return;
            }

            if (!TryUpload(denseInline, grid))
                _failedKey = key;
            else
                _last = key;
            return;
        }

        _pendingKey = key;
        _pendingGrid = grid;
        _extractTask = Task.Run(() =>
        {
            var dense = VolumeDense.ExtractDense(grid, maxResolution);

            // Wake the graph from the worker: the output slots are clean, so without this a static
            // volume never re-pulls and the finished bake is never harvested or uploaded. Same
            // wake-up the async loaders use; the eval thread owns the flag's other state, which is
            // why this bumps the version atomically instead of calling Invalidate().
            Interlocked.Increment(ref Texture.DirtyFlag.SourceVersion);
            return dense;
        });
    }

    /// <summary>Bakes up to this resolution inline on the evaluation thread instead of through the
    /// worker: a one-frame hitch is preferable to a late publish a settled graph never sees.</summary>
    private const int InlineBakeMaxResolution = 128;

    private bool TryUpload(DenseVolume dense, VdbGrid<float> grid)
    {
        Texture3D texture = null;
        Texture3dWithViews withViews = null;
        try
        {
            var description = new Texture3DDescription
                                  {
                                      Width = dense.SizeX,
                                      Height = dense.SizeY,
                                      Depth = dense.SizeZ,
                                      MipLevels = 1,
                                      Format = Format.R32_Float,
                                      BindFlags = BindFlags.ShaderResource,
                                      CpuAccessFlags = CpuAccessFlags.None,
                                      Usage = ResourceUsage.Default,
                                      OptionFlags = ResourceOptionFlags.None,
                                  };

            texture = Texture3D.CreateTexture3D(description);
            var handle = GCHandle.Alloc(dense.Data, GCHandleType.Pinned);
            try
            {
                // A 3D texture is a single subresource, so the upload has to carry every depth
                // slice. The DataBox overload sizes the copy from SlicePitch alone, which uploads
                // one slice and leaves the rest of the volume zero - the raymarcher then samples
                // 0 everywhere and reports a surface right at the camera.
                var rowPitch = dense.SizeX * sizeof(float);
                var slicePitch = rowPitch * dense.SizeY;
                var totalBytes = slicePitch * dense.SizeZ;
                unsafe
                {
                    var data = new ReadOnlySpan<byte>((void*)handle.AddrOfPinnedObject(), totalBytes);
                    ResourceManager.Device.ImmediateContext.UpdateSubresource(texture, 0, data, rowPitch, slicePitch);
                }
            }
            finally
            {
                handle.Free();
            }

            withViews = new Texture3dWithViews
                            {
                                Texture = texture,
                            };
            texture.CreateShaderResourceView(ref withViews.Srv, "VolumeTexture3D");

            Texture.Value = withViews;

            var worldMin = grid.Transform.IndexCornerToWorld(dense.Origin.ToVector3());
            var worldMax = grid.Transform.IndexCornerToWorld(dense.Origin.ToVector3()
                                                            + new Vector3(dense.SizeX - 1, dense.SizeY - 1, dense.SizeZ - 1) * dense.IndexStep
                                                            + Vector3.One);
            BoundsMin.Value = Vector3.Min(worldMin, worldMax);
            BoundsMax.Value = Vector3.Max(worldMin, worldMax);

            ReleaseTexture();
            _lastTexture = withViews;
            withViews = null; // ownership moved to _lastTexture
            texture = null;
            return true;
        }
        catch (Exception e)
        {
            // Dispose the bake that never made it into _lastTexture, and the previous one.
            withViews?.Srv?.Dispose();
            if (withViews != null)
                withViews.Texture?.Dispose();
            else
                texture?.Dispose();
            ReleaseTexture();
            ClearOutputs();
            if (_lastError != e.Message)
                Log.Warning($"VolumeToTexture3D failed: {e.Message}", this);
            _lastError = e.Message;
            return false;
        }
    }

    private void ClearOutputs()
    {
        Texture.Value = null;
        BoundsMin.Value = Vector3.Zero;
        BoundsMax.Value = Vector3.Zero;
    }

    private void ReleaseTexture()
    {
        if (_lastTexture == null)
            return;

        _lastTexture.Srv?.Dispose();
        _lastTexture.Srv = null;
        _lastTexture.Texture?.Dispose();
        _lastTexture.Texture = null;
        _lastTexture = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReleaseTexture();

        base.Dispose(disposing);
    }

    private Texture3dWithViews _lastTexture;
    private (VdbVolume, int) _last = (null, -1);
    private (VdbVolume, int) _pendingKey;
    private VdbGrid<float> _pendingGrid;
    private (VdbVolume, int) _failedKey;
    private Task<DenseVolume> _extractTask;
    private string _lastError;

    [Input(Guid = "208b3d98-a7d7-5453-96cc-b29c89d1141b")]
    public readonly InputSlot<VdbVolume> Volume = new();

    [Input(Guid = "b88c73a2-e66c-530e-9e72-a52bf2c27e13")]
    public readonly InputSlot<int> MaxResolution = new();

}
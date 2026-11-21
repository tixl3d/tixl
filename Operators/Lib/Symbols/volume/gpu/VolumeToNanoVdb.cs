using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Volume;
using T3.Core.DataTypes.Volume.Tools;
using T3.Core.DataTypes.Volume.IO;
using T3.Core.Logging;
using Lib.Utils;
using T3.Graphics.Compat;

namespace Lib.volume.gpu;

/// Flattens the volume into a NanoVDB buffer - the GPU-ready linear grid layout - and uploads it as a structured uint buffer for custom HLSL traversal. Feed the ShaderResourceView output to [NanoVdbRender] or your own shader. The Vector4 outputs carry the tree offsets relative to the buffer start: leaf / lower / upper / root.
[Guid("564e17a3-182b-5dbb-b402-088f0e717f9d")]
internal sealed class VolumeToNanoVdb : Instance<VolumeToNanoVdb>
{
    private sealed record Flattened(NanoVdbBuffer Buffer, float VoxelCount, float VoxelSize, NanoVdbBuffer.GridInfo Info);

    private readonly record struct Key(VdbVolume Volume);

    [Output(Guid = "7b63ace0-265a-54c5-90d9-239e84f7b317")]
    public readonly Slot<BufferWithViews> Buffer = new();

    [Output(Guid = "11c12656-918e-5780-a877-8042575ff67a")]
    public readonly Slot<ShaderResourceView> ShaderResourceView = new();

    [Output(Guid = "2464ac6a-e5cb-5d0d-8b51-4e916600002c")]
    public readonly Slot<Vector4> TreeOffsets = new();

    [Output(Guid = "ef5eb577-22de-57b2-a0b7-273a40b64cd4")]
    public readonly Slot<float> VoxelCount = new();

    [Output(Guid = "f6a0d0bb-7bca-5e16-a0e4-6d13f3c1ee75")]
    public readonly Slot<float> VoxelSize = new();

    [Output(Guid = "39bed154-5921-41a7-a84e-463796c20f9c")]
    public readonly Slot<Vector3> WorldMin = new();

    [Output(Guid = "2a0082da-462a-455e-af7d-e500d79f2f2a")]
    public readonly Slot<Vector3> WorldMax = new();

    public VolumeToNanoVdb()
    {
        // Any output can be the one a consumer pulls, so each must trigger Update.
        Buffer.UpdateAction = Update;
        ShaderResourceView.UpdateAction = Update;
        TreeOffsets.UpdateAction = Update;
        VoxelCount.UpdateAction = Update;
        VoxelSize.UpdateAction = Update;
        WorldMin.UpdateAction = Update;
        WorldMax.UpdateAction = Update;
        _job = new BackgroundJob<Key, Flattened>(this, nameof(VolumeToNanoVdb), Flatten,
                                                       Buffer, ShaderResourceView, TreeOffsets, VoxelCount, VoxelSize, WorldMin, WorldMax);
    }

    private void Update(EvaluationContext context)
    {
        var volume = Volume.GetValue(context);
        if (volume?.GetFloatGrid() == null)
        {
            _job.Reset();
            ClearOutputs();
            return;
        }

        // Flattening walks the whole tree, so it runs on a worker; the upload stays on this thread,
        // where GPU resources are safe to create.
        switch (_job.Poll(context, new Key(volume), out var flattened))
        {
            case BackgroundJobOutcome.Ready:
                Upload(flattened);
                break;
            case BackgroundJobOutcome.Failed:
                ClearOutputs();
                break;
        }
    }

    /// <summary>The CPU half of the conversion. Null for a grid without active voxels.</summary>
    private static Flattened Flatten(Key key)
    {
        var grid = key.Volume.GetFloatGrid();

        // An empty grid flattens with an inverted bounding box, which the raymarcher's bounds
        // intersection turns into an infinite march range - a GPU hang. It is cleared instead.
        if (grid.ActiveVoxelCount == 0)
            return null;

        var nanoBuffer = NanoVdbBuffer.FromGrid(grid);
        return new Flattened(nanoBuffer, nanoBuffer.ReadSummary().VoxelCount, grid.Transform.VoxelSize, nanoBuffer.ReadInfo());
    }

    private void Upload(Flattened flattened)
    {
        if (flattened == null)
        {
            ClearOutputs();
            return;
        }

        try
        {
            var nanoBuffer = flattened.Buffer;
            var byteCount = (nanoBuffer.Buffer.Length + 3) / 4 * 4;
            using var data = new DataStream(byteCount, true, true);
            data.Write(nanoBuffer.Buffer, 0, nanoBuffer.Buffer.Length);
            data.Position = 0;

            // Reuse one BufferWithViews so the SRV and UAV are only recreated when the buffer
            // itself is replaced; a fresh object every evaluation leaked one view per rebuild.
            ResourceManager.SetupStructuredBuffer(data, byteCount, sizeof(uint), ref _bufferWithViews.Buffer);
            ResourceManager.CreateStructuredBufferSrv(_bufferWithViews.Buffer, ref _bufferWithViews.Srv);

            // Tree node offsets, relative to the tree data start (first 32 bytes of TreeData).
            var treeStart = NanoVdbBuffer.GridDataSize;
            var leafOffset = BitConverter.ToUInt64(nanoBuffer.Buffer, treeStart);
            var lowerOffset = BitConverter.ToUInt64(nanoBuffer.Buffer, treeStart + 8);
            var upperOffset = BitConverter.ToUInt64(nanoBuffer.Buffer, treeStart + 16);
            var rootOffset = BitConverter.ToUInt64(nanoBuffer.Buffer, treeStart + 24);

            Buffer.Value = _bufferWithViews;
            ShaderResourceView.Value = _bufferWithViews.Srv;
            TreeOffsets.Value = new Vector4(leafOffset, lowerOffset, upperOffset, rootOffset);
            VoxelCount.Value = flattened.VoxelCount;
            VoxelSize.Value = flattened.VoxelSize;

            // The bounds a field needs to reject samples outside the grid; taken from the same
            // header the buffer carries, so they cannot drift from the data.
            WorldMin.Value = flattened.Info.WorldMin;
            WorldMax.Value = flattened.Info.WorldMax;
        }
        catch (Exception e)
        {
            // A failed upload (device lost, out of video memory) must not escape the update.
            Log.Warning($"VolumeToNanoVdb upload failed: {e.Message}", this);
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
        WorldMin.Value = Vector3.Zero;
        WorldMax.Value = Vector3.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _bufferWithViews.Dispose();

        base.Dispose(disposing);
    }

    private readonly BackgroundJob<Key, Flattened> _job;
    private readonly BufferWithViews _bufferWithViews = new();

    [Input(Guid = "8a677502-fd4d-5284-ab06-fd659490a4fa")]
    public readonly InputSlot<VdbVolume> Volume = new();

}
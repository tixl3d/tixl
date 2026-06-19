#nullable enable annotations

using SharpDX.Direct3D11;
using T3.Core.Resource;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.point.helper;

[Guid("FDB843E8-D859-451A-989E-F0E7A00F5E5A")]
internal sealed class ReadPointColors : Instance<ReadPointColors>, IDisposable
{
    [Output(Guid = "F3CEB246-A9F0-40BB-9C66-95E6F1FBCF22")]
    public readonly Slot<List<Vector4>> Result = new(new List<Vector4>(64));

    public ReadPointColors()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var colors = Result.Value;

        var pointBuffer = PointBuffer.GetValue(context);
        if (pointBuffer?.Buffer == null || pointBuffer.Srv == null)
        {
            colors.Clear();
            return;
        }

        var startIndex = StartIndex.GetValue(context).ClampMin(0);
        var requestedMaxCount = MaxCount.GetValue(context);
        var async = Async.GetValue(context);

        var stride = pointBuffer.Buffer.Description.StructureByteStride;
        if (stride <= 0)
            return;

        var totalElements = pointBuffer.Srv.Description.Buffer.ElementCount;
        if (startIndex >= totalElements)
        {
            colors.Clear();
            return;
        }

        var maxCount = requestedMaxCount > 0 ? requestedMaxCount : int.MaxValue;
        var outputCount = Math.Min(totalElements - startIndex, maxCount);

        if (async)
        {
            // Cache parameters used in the readback callback so it's allocation-free.
            _pendingStartIndex = startIndex;
            _pendingCount = outputCount;
            _pendingStride = stride;

            _bufferReader.InitiateRead(pointBuffer.Buffer, totalElements, stride, OnReadComplete);
            _bufferReader.Update();
            return;
        }

        // Synchronous fallback - blocks the GPU pipeline for the duration of the readback.
        EnsureSyncStaging(pointBuffer.Buffer, totalElements * stride, stride);
        var ctx = ResourceManager.Device.ImmediateContext;
        ctx.CopyResource(pointBuffer.Buffer, _syncStaging!);
        ctx.MapSubresource(_syncStaging, 0, MapMode.Read, MapFlags.None, out var stream);
        try
        {
            CopyColorsFromStream(stream, startIndex, outputCount, stride);
        }
        finally
        {
            ctx.UnmapSubresource(_syncStaging, 0);
        }
    }

    private void OnReadComplete(StructuredBufferReadAccess.ReadRequestItem item, IntPtr dataPointer, SharpDX.DataStream stream)
    {
        using (stream)
        {
            CopyColorsFromStream(stream, _pendingStartIndex, _pendingCount, _pendingStride);
        }
    }

    private void CopyColorsFromStream(SharpDX.DataStream stream, int startIndex, int outputCount, int stride)
    {
        var colors = Result.Value;
        EnsureColorListSize(colors, outputCount);
        // Color is at byte offset 32 within the Point struct (Position+F1+Orientation = 8 floats).
        const int colorOffsetInPoint = 8 * 4;
        var basePos = (long)startIndex * stride + colorOffsetInPoint;

        for (var i = 0; i < outputCount; i++)
        {
            stream.Position = basePos + i * stride;
            colors[i] = stream.Read<Vector4>();
        }
    }

    private static void EnsureColorListSize(List<Vector4> list, int targetCount)
    {
        if (list.Count == targetCount)
            return;
        if (list.Count > targetCount)
        {
            list.RemoveRange(targetCount, list.Count - targetCount);
            return;
        }
        if (list.Capacity < targetCount)
            list.Capacity = targetCount;
        for (var i = list.Count; i < targetCount; i++)
            list.Add(default);
    }

    private void EnsureSyncStaging(SharpDX.Direct3D11.Buffer source, int sizeInBytes, int stride)
    {
        if (_syncStaging != null
            && !_syncStaging.IsDisposed
            && _syncStaging.Description.SizeInBytes == sizeInBytes
            && _syncStaging.Description.StructureByteStride == stride)
        {
            return;
        }

        Utilities.Dispose(ref _syncStaging);
        var desc = new BufferDescription
                       {
                           Usage = ResourceUsage.Staging,
                           BindFlags = BindFlags.None,
                           SizeInBytes = sizeInBytes,
                           OptionFlags = ResourceOptionFlags.BufferStructured,
                           StructureByteStride = stride,
                           CpuAccessFlags = CpuAccessFlags.Read,
                       };
        _syncStaging = new SharpDX.Direct3D11.Buffer(ResourceManager.Device, desc);
    }

    void IDisposable.Dispose()
    {
        _bufferReader.Dispose();
        Utilities.Dispose(ref _syncStaging);
    }

    private readonly StructuredBufferReadAccess _bufferReader = new();
    private SharpDX.Direct3D11.Buffer? _syncStaging;
    private int _pendingStartIndex;
    private int _pendingCount;
    private int _pendingStride;

    [Input(Guid = "0406E85A-61C4-42CE-A174-8D683D2801B5")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> PointBuffer = new();

    [Input(Guid = "99BC9DC4-D416-4D50-9730-13E829C14BF2")]
    public readonly InputSlot<int> StartIndex = new(0);

    [Input(Guid = "7D40F8E1-CB43-4389-A12F-126160AB7E2C")]
    public readonly InputSlot<int> MaxCount = new(50);

    [Input(Guid = "B038C123-CDAC-4E6D-9863-3446631DFED5")]
    public readonly InputSlot<bool> Async = new(true);
}

using System;
using System.Numerics;
using T3.Core.DataTypes;

namespace Lib.laser.convert;

/// <summary>
/// Converts an ILDA-style laser point list into a GPU structured buffer so laser frames can be
/// drawn or fed back into a scene. Positions are scaled by <see cref="Scale"/>; RGBA carries
/// R/G/B and the blanking gate I. The buffer keeps a capacity with a separator-filled tail, so a
/// frame whose length changes does not recreate GPU resources every evaluation. Allocation and
/// upload are bounded to <c>MaxPoints</c>; every failure degrades to an empty buffer instead of
/// escaping into TiXL's evaluator.
/// </summary>
[Guid("624747a6-e974-4650-b60c-a7bdb078a104")]
internal sealed class LaserPointsToBuffer : Instance<LaserPointsToBuffer>
{
    [Output(Guid = "61bb56cf-8b97-468b-8811-84dd3fdc3649")]
    public readonly Slot<BufferWithViews> Buffer = new();

    [Output(Guid = "fcee2f07-b573-4dd0-a365-36bad0cce3c5")]
    public readonly Slot<int> PointCount = new();

    public LaserPointsToBuffer()
    {
        Buffer.UpdateAction += Update;
        PointCount.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        // Any exception (GPU, network, corrupted upstream) must not escape to TiXL's evaluator.
        try
        {
            var inputPoints = LaserPoints.GetValue(context);
            if (inputPoints is not StructuredList<LaserPoint> laserPoints
                || laserPoints.TypedElements == null
                || laserPoints.NumElements == 0)
            {
                PointCount.Value = 0;
                PointCount.DirtyFlag.Invalidate();
                Buffer.Value = null;
                return;
            }

            var scale = Scale.GetValue(context);
            if (!float.IsFinite(scale) || scale == 0f)
                scale = DefaultScale; // an unconnected slot used to collapse every point to the origin

            var source = laserPoints.TypedElements;
            // Glitchy input (e.g. malformed network frames) must not allocate a gigantic GPU buffer.
            var count = Math.Min(laserPoints.NumElements, MaxPoints);

            // Build Point array with current scale applied. The array always has exactly the length
            // of the GPU buffer, so the upload matches the allocation (avoids native access
            // violations); the tail beyond the frame holds separators, which are never drawn.
            EnsureCapacity(count);
            for (var i = count; i < _filledCount; i++)
                _points[i] = T3.Core.DataTypes.Point.Separator();

            _filledCount = count;

            for (var i = 0; i < count; i++)
            {
                var lp = source[i];
                // Use the full ctor so untouched fields match the Point conventions (F1/F2 = 1,
                // Scale = 1, valid Orientation). Zero-initialized FX/Scale fields collapse points
                // to zero size in DrawPoints when UsePointScale or ScaleFX is enabled.
                _points[i] = new T3.Core.DataTypes.Point
                                 {
                                     Position = new Vector3(lp.X * scale, lp.Y * scale, 0),
                                     Color = new Vector4(
                                         lp.R / 65535f,
                                         lp.G / 65535f,
                                         lp.B / 65535f,
                                         lp.I / 65535f
                                     )
                                 };
            }

            // Upload to GPU buffer
            if (!UpdateBuffer(_points.Length))
            {
                PointCount.Value = 0;
                PointCount.DirtyFlag.Invalidate();
                Buffer.Value = null;
                return;
            }

            PointCount.Value = count;
            PointCount.DirtyFlag.Invalidate();
            Buffer.Value = _bufferWithViews;
        }
        catch (Exception e)
        {
            Log.Error($"LaserPointsToBuffer: update failed – {e.Message}", this);
            PointCount.Value = 0;
            PointCount.DirtyFlag.Invalidate();
            Buffer.Value = null;
        }
    }

    /// <summary>
    /// Laser frames change length from frame to frame. The buffer only grows (with headroom) and shrinks
    /// after the content has stayed small for a while, so a changing point count does not create and
    /// destroy GPU resources every frame.
    /// </summary>
    private void EnsureCapacity(int count)
    {
        var current = _points?.Length ?? 0;
        if (count > current)
        {
            Resize(Math.Min(MaxPoints, Math.Max(MinCapacity, count + count / 4)));
            _slackFrames = 0;
            return;
        }

        if (current <= MinCapacity || count * 4 > current)
        {
            _slackFrames = 0;
            return;
        }

        if (++_slackFrames < ShrinkAfterFrames)
            return;

        _slackFrames = 0;
        Resize(Math.Max(MinCapacity, count + count / 4));
    }

    private void Resize(int capacity)
    {
        _points = new T3.Core.DataTypes.Point[capacity];
        var separator = T3.Core.DataTypes.Point.Separator();
        Array.Fill(_points, separator);
        _filledCount = 0;
    }

    private bool UpdateBuffer(int count)
    {
        // Any GPU failure here must never escape Update: TiXL terminates on unhandled operator
        // exceptions. Create an empty structured buffer, then upload via UpdateSubresource -
        // never via CreateBuffer-with-initial-data, whose native copy has crashed the process
        // (uncatchable access violation inside the driver).
        try
        {
            var totalBytes = count * T3.Core.DataTypes.Point.Stride;
            if (_buffer == null || _buffer.Description.SizeInBytes != totalBytes)
            {
                ReleaseBuffer();
                ResourceManager.SetupStructuredBuffer(totalBytes, T3.Core.DataTypes.Point.Stride, ref _buffer);
                if (_buffer == null)
                {
                    Log.Error("Failed to create laser point buffer", this);
                    return false;
                }

                ResourceManager.CreateStructuredBufferSrv(_buffer, ref _bufferWithViews.Srv);
                if (_bufferWithViews.Srv == null)
                {
                    Log.Error("Failed to create the SRV for the laser point buffer", this);
                    ReleaseBuffer();
                    return false;
                }

                // The UAV is optional: drawing only needs the SRV, and some graphics backends
                // (and the newer resource layer) intentionally do not create a UAV for a
                // draw-only structured buffer. Requiring it used to drop an otherwise valid
                // frame on every evaluation.
                try
                {
                    ResourceManager.CreateStructuredBufferUav(_buffer, UnorderedAccessViewBufferFlags.None, ref _bufferWithViews.Uav);
                }
                catch (Exception e)
                {
                    _bufferWithViews.Uav = null;
                    Log.Debug($"Laser point buffer UAV unavailable ({e.Message}) - SRV-only buffer kept");
                }

                _bufferWithViews.Buffer = _buffer;
            }

            // Upload only the written prefix. The array overload copies the whole capacity (which can be
            // 64 MB after one burst) even when this frame produced a handful of points.
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(_points.AsSpan(0, count));
            ResourceManager.Device.ImmediateContext.UpdateSubresource(_buffer, 0, bytes, 0, 0);
            // Laser-frame data drawn back into a scene is a preview - mark the buffer so a
            // LaserCamera capturing that scene never captures its own output (feedback echo).
            T3.Core.Rendering.GeometryCapture.MarkExcludedFromCapture(_buffer);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to upload laser point buffer: {e.Message} - releasing GPU resources, dropping this frame.", this);
            ReleaseBuffer();
            return false;
        }
    }

    private void ReleaseBuffer()
    {
        _bufferWithViews.Srv?.Dispose();
        _bufferWithViews.Uav?.Dispose();
        _buffer?.Dispose();
        _bufferWithViews.Srv = null;
        _bufferWithViews.Uav = null;
        _bufferWithViews.Buffer = null;
        _buffer = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReleaseBuffer();

        base.Dispose(disposing);
    }

    private Buffer _buffer;
    private readonly BufferWithViews _bufferWithViews = new();
    private T3.Core.DataTypes.Point[] _points;
    private int _filledCount;
    private int _slackFrames;

    private const int MinCapacity = 256;
    private const int ShrinkAfterFrames = 120;

    /// <summary>ILDA units -> roughly [-1, 1] world units (the value authored by the .t3 asset).</summary>
    private const float DefaultScale = 1f / 32767f;

    /// <summary>Hard cap on converted points (1M points = 64 MB GPU buffer) - bounds damage from corrupt input.</summary>
    private const int MaxPoints = 1 << 20;

    /// <summary>Laser frame to convert. Blanked points (I = 0) are preserved as zero-alpha entries.</summary>
    [Input(Guid = "55ed02f0-2cd5-4566-b15a-580fddda2484")]
    public readonly InputSlot<StructuredList> LaserPoints = new();

    /// <summary>Multiplier applied to X/Y. The default maps the ILDA ±32767 field to roughly ±1;
    /// non-finite or zero values fall back to it so the buffer can never collapse to the origin.</summary>
    [Input(Guid = "112189ef-6929-4cee-b54b-c2dfafa12ee5")]
    public readonly InputSlot<float> Scale = new(DefaultScale);
}

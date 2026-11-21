using System;
using System.Threading;
using System.Threading.Tasks;
using T3.Graphics;
using T3.Graphics.Compat;
using T3.Core.Logging;
using T3.Core.Resource;

#pragma warning disable CS8981

namespace Lib.io.video.depthanything;

/// <summary>
/// Creates a binary mask from depth data based on threshold ranges
/// Useful for foreground/background segmentation based on depth
/// </summary>
[Guid("f4a5b6c7-d8e9-f0a1-b2c3-d4e5f6a7b8c9")]
public class DepthThreshold : Instance<DepthThreshold>
{
    #region Outputs

    [Output(Guid = "a5b6c7d8-e9f0-a1b2-c3d4-e5f6a7b8c9d0", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D?> MaskTexture = new();

    [Output(Guid = "b6c7d8e9-f0a1-b2c3-d4e5-f6a7b8c9d0e1", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> PixelRatio = new();

    #endregion

    public DepthThreshold()
    {
        MaskTexture.UpdateAction = Update;
        PixelRatio.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var depthTexture = DepthTexture.GetValue(context);
        var minThreshold = MinThreshold.GetValue(context);
        var maxThreshold = MaxThreshold.GetValue(context);
        var invert = Invert.GetValue(context);
        var useNormalized = UseNormalized.GetValue(context);

        if (depthTexture == null || depthTexture.IsDisposed)
        {
            ResetReadback();
            MaskTexture.Value = null;
            PixelRatio.Value = 0;
            return;
        }

        var desc = depthTexture.Description;
        if (desc.Format != Format.R32_Float)
        {
            if (!_loggedUnsupportedFormat)
            {
                _loggedUnsupportedFormat = true;
                Log.Warning($"[DepthThreshold] Unsupported depth format {desc.Format}; expected R32_Float.", this);
            }

            ResetReadback();
            MaskTexture.Value = null;
            PixelRatio.Value = 0;
            return;
        }

        EnsureMaskTexture(desc.Width, desc.Height);

        // Read the previous frame's copy first. A map that isn't ready costs nothing and keeps the last
        // mask; never issue a new copy over a pending one, or the readback can be starved forever.
        if (_readbackPending && !TryCompleteReadback())
            return;

        // Only one copy in flight: this is the frame-to-frame back-pressure that keeps the render thread
        // from queueing GPU work faster than it can be read back.
        var stagingTexture = GetOrCreateStagingTexture(desc.Width, desc.Height, desc.Format);
        ResourceManager.Device.ImmediateContext.CopyResource(depthTexture, stagingTexture);

        _readbackPending = true;
        _waitFrames = 0;
        _pendingWidth = desc.Width;
        _pendingHeight = desc.Height;
        _pendingMinThreshold = minThreshold;
        _pendingMaxThreshold = maxThreshold;
        _pendingInvert = invert;
        _pendingUseNormalized = useNormalized;
    }

    private bool TryCompleteReadback()
    {
        var stagingTexture = _stagingTexture;
        if (stagingTexture == null || stagingTexture.IsDisposed || stagingTexture.Native == null)
        {
            _readbackPending = false;
            return true;
        }

        // Non-blocking map: a device reset or a saturated queue leaves this false until the copy retires.
        if (!ResourceManager.Device.Backend.TryMapForRead(stagingTexture.Native, 0, out var mapped))
        {
            _waitFrames++;
            if (_waitFrames <= ReadbackTimeoutFrames)
                return false;

            // A device reset can leave the copy unfinished forever; drop it and start over next frame.
            _readbackPending = false;
            return true;
        }

        try
        {
            var width = _pendingWidth;
            var height = _pendingHeight;
            var pixelCount = width * height;
            if (pixelCount <= 0 || mapped.SizeInBytes <= 0)
                return true;

            if (_maskData == null || _maskData.Length != pixelCount)
                _maskData = new byte[pixelCount];

            var actualMin = _pendingMinThreshold;
            var actualMax = _pendingMaxThreshold;

            if (_pendingUseNormalized)
            {
                var dataMin = float.MaxValue;
                var dataMax = float.MinValue;
                unsafe
                {
                    for (var y = 0; y < height; y++)
                    {
                        var row = (float*)((byte*)mapped.Data + (long)y * mapped.RowPitch);
                        for (var x = 0; x < width; x++)
                        {
                            var d = row[x];
                            if (float.IsNaN(d) || float.IsInfinity(d))
                                continue;

                            if (d < dataMin) dataMin = d;
                            if (d > dataMax) dataMax = d;
                        }
                    }
                }

                var range = dataMax - dataMin;
                if (range > 0.0001f)
                {
                    actualMin = dataMin + _pendingMinThreshold * range;
                    actualMax = dataMin + _pendingMaxThreshold * range;
                }
                else
                {
                    actualMin = dataMin;
                    actualMax = dataMax;
                }
            }

            var maskData = _maskData;
            var maskTexture = _maskTexture;
            if (maskData == null || maskTexture == null)
                return true;

            var invert = _pendingInvert;
            var passCount = 0;

            // Per-row partitions keep the inner loop free of Interlocked traffic (one atomic per row).
            Parallel.For(0, height,
                         () => 0,
                         (y, _, localCount) =>
                         {
                             unsafe
                             {
                                 var row = (float*)((byte*)mapped.Data + (long)y * mapped.RowPitch);
                                 var offset = y * width;
                                 for (var x = 0; x < width; x++)
                                 {
                                     var depth = row[x];
                                     var inRange = depth >= actualMin && depth <= actualMax &&
                                                   !float.IsNaN(depth) && !float.IsInfinity(depth);
                                     if (invert) inRange = !inRange;

                                     maskData[offset + x] = inRange ? (byte)255 : (byte)0;
                                     if (inRange) localCount++;
                                 }
                             }

                             return localCount;
                         },
                         localCount => Interlocked.Add(ref passCount, localCount));

            var handle = System.Runtime.InteropServices.GCHandle.Alloc(maskData, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var dataOut = new DataBox(handle.AddrOfPinnedObject(), width, 0);
                ResourceManager.Device.ImmediateContext.UpdateSubresource(dataOut, maskTexture);
            }
            finally
            {
                handle.Free();
            }

            MaskTexture.Value = _maskTexture;
            PixelRatio.Value = pixelCount > 0 ? (float)passCount / pixelCount : 0f;
            return true;
        }
        finally
        {
            ResourceManager.Device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
            _readbackPending = false;
        }
    }

    private void EnsureMaskTexture(int width, int height)
    {
        if (_maskTexture != null && _maskTexture.Description.Width == width && _maskTexture.Description.Height == height)
            return;

        _maskTexture?.Dispose();
        var maskDesc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            OptionFlags = ResourceOptionFlags.None
        };
        _maskTexture = new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, maskDesc));
    }

    /// <summary>One staging texture, replaced on size/format change: a resized input must not accumulate buffers.</summary>
    private T3.Graphics.Compat.Texture2D GetOrCreateStagingTexture(int width, int height, Format format)
    {
        if (_stagingTexture is { IsDisposed: false }
            && _stagingTexture.Description.Width == width
            && _stagingTexture.Description.Height == height
            && _stagingTexture.Description.Format == format)
            return _stagingTexture;

        ResetReadback();
        _stagingTexture?.Dispose();
        _stagingTexture = new Texture2D(new T3.Graphics.Compat.Texture2D(ResourceManager.Device, new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        }));

        return _stagingTexture;
    }

    private void ResetReadback()
    {
        // A pending copy is never left mapped; only the boolean and the staging texture need resetting.
        _readbackPending = false;
        _waitFrames = 0;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing) return;

        _maskTexture?.Dispose();
        _maskTexture = null;

        _stagingTexture?.Dispose();
        _stagingTexture = null;

        base.Dispose(isDisposing);
    }

    #region Inputs

    [Input(Guid = "a3b4c5d6-e7f8-a9b0-c1d2-e3f4a5b6c7d8")]
    public readonly InputSlot<Texture2D> DepthTexture = new();

    [Input(Guid = "b4c5d6e7-f8a9-b0c1-d2e3-f4a5b6c7d8e9")]
    public readonly InputSlot<float> MinThreshold = new(0.0f);

    [Input(Guid = "c5d6e7f8-a9b0-c1d2-e3f4-a5b6c7d8e9f0")]
    public readonly InputSlot<float> MaxThreshold = new(0.5f);

    [Input(Guid = "d6e7f8a9-b0c1-d2e3-f4a5-b6c7d8e9f0a1")]
    public readonly InputSlot<bool> Invert = new(false);

    [Input(Guid = "e7f8a9b0-c1d2-e3f4-a5b6-c7d8e9f0a1b2")]
    public readonly InputSlot<bool> UseNormalized = new(true);

    #endregion

    #region Private Fields

    private const int ReadbackTimeoutFrames = 60;

    private Texture2D? _maskTexture;
    private T3.Graphics.Compat.Texture2D? _stagingTexture;
    private byte[]? _maskData;
    private bool _readbackPending;
    private bool _loggedUnsupportedFormat;
    private int _waitFrames;
    private int _pendingWidth;
    private int _pendingHeight;
    private float _pendingMinThreshold;
    private float _pendingMaxThreshold;
    private bool _pendingInvert;
    private bool _pendingUseNormalized;

    #endregion
}

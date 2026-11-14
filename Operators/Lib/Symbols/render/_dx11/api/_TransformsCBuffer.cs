#nullable enable
using Lib.Utils;
using T3.Core.Animation;
using T3.Core.Rendering;

namespace Lib.render._dx11.api;

[Guid("a60adc26-d7c6-4615-af78-8d2d6da46b79")]
internal sealed class _TransformsCBuffer : Instance<_TransformsCBuffer>
{
    /** The transforms one evaluation of a frame wrote this frame and the frame before. */
    private sealed class TransformBuffers : IDisposable
    {
        public Buffer? Current;
        public Buffer? Previous;

        public void Dispose()
        {
            Current?.Dispose();
            Current = null;
            Previous?.Dispose();
            Previous = null;
        }
    }

    [Output(Guid = "7A76D147-4B8E-48CF-AA3E-AAC3AA90E888", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Buffer?> Buffer = new();

    [Output(Guid = "A200CC39-8FA3-4467-BC8F-EB03731A1ECE", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Buffer?> PrevBuffer = new();


    public _TransformsCBuffer()
    {
        Buffer.UpdateAction += Update;
        _history = new EvaluationHistory<TransformBuffers>(CreateTransformBuffers, MaxCallsPerPass);
    }

    private void Update(EvaluationContext context)
    {
        // Loop iterations, motion blur passes, cube map faces and other windows evaluate this op several times per
        // frame. Each evaluation keeps its own history, so Prev is the same evaluation one frame earlier.
        var hasSlot = _history.TryGetSlot(context, out var buffers, out var isFirstEvaluation, out var previousSeenFrame);
        if (!hasSlot)
        {
            buffers = _beyondCallLimit;
        }

        var isFirstUse = buffers!.Current == null || buffers.Current.IsDisposed;
        if (isFirstUse)
        {
            buffers.Dispose();
            buffers.Current = CreateConstBuffer("A");
            buffers.Previous = CreateConstBuffer("B");
        }
        else
        {
            (buffers.Current, buffers.Previous) = (buffers.Previous, buffers.Current);
        }

        var hasCam = TryGetCamera(context, out var camera);

        var data = hasCam
            ? new TransformBufferLayout(camera!.CameraToClipSpace, camera.WorldToCamera, context.ObjectToWorld)
            : new TransformBufferLayout(context.CameraToClipSpace, context.WorldToCamera, context.ObjectToWorld);

        ResourceManager.UpdateConstBuffer(data, buffers.Current!);

        // Without last frame's matrices (new evaluation, skipped frames, beyond the call limit), report no motion
        // instead of reprojecting through matrices that are uninitialized or seconds old.
        var hasPreviousFrame = hasSlot && !isFirstUse && previousSeenFrame == Playback.FrameCount - 1;
        if (!hasPreviousFrame)
            ResourceManager.UpdateConstBuffer(data, buffers.Previous!);

        Buffer.Value = buffers.Current;
        PrevBuffer.Value = buffers.Previous;
        Buffer.DirtyFlag.Clear();
        PrevBuffer.DirtyFlag.Clear();
    }

    private static TransformBuffers CreateTransformBuffers()
    {
        return new TransformBuffers();
    }

    private static Buffer CreateConstBuffer(string suffix)
    {
        int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TransformBufferLayout));
        size = (size + 15) & ~15; // 16-byte alignment for CBs

        return new Buffer(ResourceManager.Device, size,
                          ResourceUsage.Default,
                          BindFlags.ConstantBuffer,
                          CpuAccessFlags.None,
                          ResourceOptionFlags.None,
                          0)
                   {
                       DebugName = nameof(_TransformsCBuffer) + "_" + suffix
                   };
    }

    private bool TryGetCamera(EvaluationContext context, [NotNullWhen(true)] out ICameraPropertiesProvider? camera)
    {
        camera = null;
        if (!CameraReference.HasInputConnections)
        {
            CameraReference.DirtyFlag.Clear();
            return false;
        }

        if (CameraReference.GetValue(context) is not ICameraPropertiesProvider camera2)
        {
            return false;
        }

        camera = camera2;
        return true;
    }


    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        _history.Dispose();
        _beyondCallLimit.Dispose();
    }


    [Input(Guid = "55DBF5B7-B3D2-4D61-86D5-AC3B167244B7")]
    public readonly InputSlot<Object> CameraReference = new();

    /** Bounds the history of an op inside a long loop; evaluations beyond it report no motion. */
    private const int MaxCallsPerPass = 256;

    private readonly EvaluationHistory<TransformBuffers> _history;

    /** Shared by all evaluations beyond <see cref="MaxCallsPerPass"/>; holds no history. */
    private readonly TransformBuffers _beyondCallLimit = new();
}

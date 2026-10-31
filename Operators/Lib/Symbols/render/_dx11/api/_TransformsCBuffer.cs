#nullable enable
using T3.Core.Animation;
using T3.Core.Rendering;

namespace Lib.render._dx11.api;

[Guid("a60adc26-d7c6-4615-af78-8d2d6da46b79")]
internal sealed class _TransformsCBuffer : Instance<_TransformsCBuffer>
{
    /** The transforms one repeated pass of a frame wrote this frame and the frame before. */
    private sealed class PassBuffers
    {
        public Buffer? Current;
        public Buffer? Previous;
        public int LastUpdateFrame = -1;

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
    }

    private void Update(EvaluationContext context)
    {
        // Cube map faces and motion blur samples re-evaluate within one frame with different cameras.
        // Each pass keeps its own history, so Prev is the same view one frame earlier and not the previous pass.
        context.IntVariables.TryGetValue(RepeatPassVariableName, out var passIndex);
        var pass = GetPassBuffers(Math.Clamp(passIndex, 0, MaxRepeatPasses - 1));

        var isFirstUse = pass.Current == null || pass.Current.IsDisposed;
        if (isFirstUse)
        {
            pass.Dispose();
            pass.Current = CreateConstBuffer("A");
            pass.Previous = CreateConstBuffer("B");
        }
        else if (pass.LastUpdateFrame != Playback.FrameCount)
        {
            (pass.Current, pass.Previous) = (pass.Previous, pass.Current);
        }

        pass.LastUpdateFrame = Playback.FrameCount;

        var hasCam = TryGetCamera(context, out var camera);

        var data = hasCam
            ? new TransformBufferLayout(camera!.CameraToClipSpace, camera.WorldToCamera, context.ObjectToWorld)
            : new TransformBufferLayout(context.CameraToClipSpace, context.WorldToCamera, context.ObjectToWorld);

        ResourceManager.UpdateConstBuffer(data, pass.Current!);

        // Without history, report no motion instead of reprojecting through uninitialized matrices.
        if (isFirstUse)
            ResourceManager.UpdateConstBuffer(data, pass.Previous!);

        Buffer.Value = pass.Current;
        PrevBuffer.Value = pass.Previous;
        Buffer.DirtyFlag.Clear();
        PrevBuffer.DirtyFlag.Clear();
    }

    private PassBuffers GetPassBuffers(int passIndex)
    {
        if (passIndex >= _passes.Length)
            Array.Resize(ref _passes, passIndex + 1);

        return _passes[passIndex] ??= new PassBuffers();
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
        foreach (var pass in _passes)
        {
            pass?.Dispose();
        }
    }


    [Input(Guid = "55DBF5B7-B3D2-4D61-86D5-AC3B167244B7")]
    public readonly InputSlot<Object> CameraReference = new();

    private const string RepeatPassVariableName = "__MotionBlurPass";

    /** Bounds the per-pass history against unexpected pass indices. */
    private const int MaxRepeatPasses = 64;

    private PassBuffers?[] _passes = new PassBuffers?[1];
}

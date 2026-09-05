#nullable enable
using T3.Core.Rendering;

namespace Lib.render._dx11.api;

[Guid("a60adc26-d7c6-4615-af78-8d2d6da46b79")]
internal sealed class _TransformsCBuffer : Instance<_TransformsCBuffer>
{
    [Output(Guid = "7A76D147-4B8E-48CF-AA3E-AAC3AA90E888", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Buffer?> Buffer = new();

    [Output(Guid = "A200CC39-8FA3-4467-BC8F-EB03731A1ECE", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Buffer?> PrevBuffer = new();


    public _TransformsCBuffer()
    {
        Buffer.UpdateAction += Update;
    }

    private void EnsureAllocated()
    {
        if (_cbA != null && !_cbA.IsDisposed) return;

        var dev = ResourceManager.Device;

        int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TransformBufferLayout));
        size = (size + 15) & ~15; // 16-byte alignment for CBs

        _cbA = new Buffer(dev, size,
            ResourceUsage.Default,
            BindFlags.ConstantBuffer,
            CpuAccessFlags.None,
            ResourceOptionFlags.None,
            0);

        _cbB = new Buffer(dev, size,
            ResourceUsage.Default,
            BindFlags.ConstantBuffer,
            CpuAccessFlags.None,
            ResourceOptionFlags.None,
            0);

        _cbA.DebugName = nameof(_TransformsCBuffer) + "_A";
        _cbB.DebugName = nameof(_TransformsCBuffer) + "_B";
    }

    private void Update(EvaluationContext context)
    {
        EnsureAllocated();


        // Swap roles: _current will be written this frame, _previous is what we output as ‘Prev’
        var current = _toggle ? _cbA : _cbB;
        var previous = _toggle ? _cbB : _cbA;

        var hasCam = TryGetCamera(context, out var camera);

        // Write *current* with this frame’s data
        var data = hasCam
            ? new TransformBufferLayout(camera!.CameraToClipSpace, camera.WorldToCamera, context.ObjectToWorld)
            : new TransformBufferLayout(context.CameraToClipSpace, context.WorldToCamera, context.ObjectToWorld);

        if(current != null)
            ResourceManager.UpdateConstBuffer(data, current);

        // Expose buffers
        Buffer.Value = current;
        PrevBuffer.Value = previous;
        Buffer.DirtyFlag.Clear();
        PrevBuffer.DirtyFlag.Clear();

        _toggle = !_toggle;
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
        _cbA?.Dispose();
        _cbA = null;
        _cbB?.Dispose();
        _cbB = null;
    }


    [Input(Guid = "55DBF5B7-B3D2-4D61-86D5-AC3B167244B7")]
    public readonly InputSlot<Object> CameraReference = new();


    private Buffer? _cbA, _cbB; // ping-pong
    private bool _toggle;
}
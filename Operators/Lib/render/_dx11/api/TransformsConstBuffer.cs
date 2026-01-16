using SharpDX.Direct3D11;
using T3.Core.Rendering;

namespace Lib.render._dx11.api;

[Guid("a60adc26-d7c6-4615-af78-8d2d6da46b79")]
internal sealed class TransformsConstBuffer : Instance<TransformsConstBuffer>
{
    [Output(Guid = "7A76D147-4B8E-48CF-AA3E-AAC3AA90E888", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Buffer> Buffer = new();

    [Output(Guid = "A200CC39-8FA3-4467-BC8F-EB03731A1ECE", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Buffer> PrevBuffer = new();

    
    public TransformsConstBuffer()
    {
        Buffer.UpdateAction += Update;
    }
    
    private void EnsureAllocated()
    {
            if (_cbA != null && !_cbA.IsDisposed) return;

            var dev  = ResourceManager.Device;

            int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(TransformBufferLayout));
            size = (size + 15) & ~15;                    // 16-byte alignment for CBs

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

            _cbA.DebugName = nameof(TransformsConstBuffer) + "_A";
            _cbB.DebugName = nameof(TransformsConstBuffer) + "_B";
    }

    private void Update(EvaluationContext context)
    {
        EnsureAllocated();
        

        // Swap roles: _current will be written this frame, _previous is what we output as ‘Prev’
        Buffer current = _toggle ? _cbA : _cbB;
        Buffer previous = _toggle ? _cbB : _cbA;

        // Write *current* with this frame’s data
        var data = new TransformBufferLayout(context.CameraToClipSpace,
                                             context.WorldToCamera,
                                             context.ObjectToWorld);
        ResourceManager.UpdateConstBuffer(data, current);

        // Expose buffers
        Buffer.Value = current;
        PrevBuffer.Value = previous;
        Buffer.DirtyFlag.Clear();
        PrevBuffer.DirtyFlag.Clear();

        _toggle = !_toggle;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        _cbA?.Dispose(); _cbA = null;
        _cbB?.Dispose(); _cbB = null;
    }    
    
    private Buffer _cbA, _cbB;     // ping-pong
    private bool _toggle;
    
    // private void Update(EvaluationContext context)
    // {
    //     PrevBuffer.Value = Buffer.Value;
    //
    //     ResourceManager.SetupConstBuffer(new TransformBufferLayout(context.CameraToClipSpace, 
    //                                                                context.WorldToCamera, 
    //                                                                context.ObjectToWorld), 
    //                                      ref Buffer.Value);
    //     Buffer.Value.DebugName = nameof(TransformsConstBuffer);
    // }
}

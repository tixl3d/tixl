using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// What SharpDX's <c>Device</c> was: the thing resources are created from. Creation is thread-safe — TiXL
/// loads thumbnails, decodes video and builds buffers off the render thread — while
/// <see cref="ImmediateContext"/> belongs to the main thread alone.
/// </summary>
public sealed class Device(IGraphicsBackend backend)
{
    public IGraphicsBackend Backend { get; } = backend;

    public DeviceContext ImmediateContext { get; } = new(backend);

    public string AdapterName => Backend.AdapterName;

    /// <summary>
    /// Starts the frame the immediate context records into. The render loops call this; operators never do.
    /// Without it there is nowhere for the upload ring to reset and no point at which dead resources retire.
    /// </summary>
    public void BeginFrame() => ImmediateContext.BeginFrame();

    public void EndFrame() => ImmediateContext.EndFrame();
}

/// <summary>
/// CPU memory handed to an upload. Replaces SharpDX's <c>DataBox</c> with the same three fields, so the call
/// sites that read <c>DataPointer</c> and <c>RowPitch</c> migrate unchanged.
/// </summary>
public readonly unsafe struct DataBox(IntPtr dataPointer, int rowPitch, int slicePitch)
{
    public readonly IntPtr DataPointer = dataPointer;
    public readonly int RowPitch = rowPitch;
    public readonly int SlicePitch = slicePitch;

    public bool IsEmpty => DataPointer == IntPtr.Zero;

    internal DataBox(in MappedMemory mapped) : this((IntPtr)mapped.Data, mapped.RowPitch, mapped.SlicePitch)
    {
    }
}

/// <summary>The two-dimensional form, for texture uploads.</summary>
public readonly struct DataRectangle(IntPtr dataPointer, int pitch)
{
    public readonly IntPtr DataPointer = dataPointer;
    public readonly int Pitch = pitch;
}

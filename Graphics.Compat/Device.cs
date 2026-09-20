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
    /// Fixed at 11.1: everything the facade exposes is a D3D11.1 feature, and the Vulkan backend requires
    /// more than that anyway. The DDS loader branches on it to decide whether a format is allowed.
    /// </summary>
    public FeatureLevel FeatureLevel => FeatureLevel.Level_11_1;

    /// <summary>The native device handle the video and capture libraries take. Zero where there is none.</summary>
    public IntPtr NativePointer => Backend.NativeDeviceHandle;

    public void SetMultithreadProtected(bool enabled) => Backend.SetMultithreadProtected(enabled);

    /// <summary>Wraps a texture another library created, such as a decoder surface.</summary>
    public Texture2D? AdoptTexture(IntPtr nativeHandle, Texture2DDescription description)
    {
        var texture = Backend.AdoptTexture(nativeHandle, Texture2D.Describe(description), "adopted");
        return texture == null ? null : new Texture2D(this, texture, description);
    }

    /// <summary>
    /// What a format can be used for. The DDS loader asks before it picks a format, and a backend that
    /// cannot do something is better than a texture that silently fails to bind.
    /// </summary>
    public FormatSupport CheckFormatSupport(Format format)
    {
        var support = FormatSupport.None;

        if (Backend.Supports(format, TextureUsage.Sampled))
            support |= FormatSupport.Texture2D | FormatSupport.Texture3D | FormatSupport.TextureCube | FormatSupport.ShaderSample;

        if (Backend.Supports(format, TextureUsage.RenderTarget))
            support |= FormatSupport.RenderTarget;

        if (Backend.Supports(format, TextureUsage.DepthStencil))
            support |= FormatSupport.DepthStencil;

        if (Backend.Supports(format, TextureUsage.Storage))
            support |= FormatSupport.TypedUnorderedAccessView;

        return support;
    }

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
public unsafe struct DataBox(IntPtr dataPointer, int rowPitch, int slicePitch)
{
    public IntPtr DataPointer = dataPointer;
    public int RowPitch = rowPitch;
    public int SlicePitch = slicePitch;

    public bool IsEmpty => DataPointer == IntPtr.Zero;

    internal DataBox(in MappedMemory mapped) : this((IntPtr)mapped.Data, mapped.RowPitch, mapped.SlicePitch)
    {
    }
}

/// <summary>The two-dimensional form, for texture uploads.</summary>
public struct DataRectangle(IntPtr dataPointer, int pitch)
{
    public IntPtr DataPointer = dataPointer;
    public int Pitch = pitch;
}

using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// What D3D11 calls a device child: anything the device created. TiXL constrains on it where a member is
/// common to shaders and resources alike.
/// </summary>
public abstract class DeviceChild : IDisposable
{
    /// <summary>Shows up in RenderDoc and in validation messages.</summary>
    public abstract string? DebugName { get; set; }

    public abstract void Dispose();
}

/// <summary>
/// Base for the D3D11-shaped resources. Everything here is a thin wrapper around a backend handle: the
/// wrapper carries the D3D11 description operators read back, the handle does the work.
/// </summary>
public abstract class Resource : DeviceChild
{
    protected Resource(Device device)
    {
        Device = device;
        Id = Interlocked.Increment(ref _nextId);
    }

    public Device Device { get; }

    /// <summary>
    /// Identity for the view caches that keyed on <c>NativePointer</c> before. Unique and stable for the
    /// lifetime of the resource — and, unlike a native pointer, not reused by the next allocation.
    /// </summary>
    public ulong Id { get; }

    public override string? DebugName
    {
        get => Native?.Label;
        set
        {
            if (Native != null)
                Native.Label = value;
        }
    }

    public bool IsDisposed => Native == null || Native.IsDisposed;

    /// <summary>The backend object. Null only if creation failed, which on Vulkan can happen under memory pressure.</summary>
    public abstract GpuResource? Native { get; }

    /// <summary>
    /// The native object, for the libraries TiXL hands resources to — FFmpeg's decoder, Spout, NDI. Zero on
    /// a backend whose objects are not COM pointers, and those features are Windows-only anyway.
    /// </summary>
    public IntPtr NativePointer => Native?.NativeHandle ?? IntPtr.Zero;

    /// <summary>The largest 2D texture D3D11 feature level 11 allows, which operators clamp against.</summary>
    public const int MaximumTexture2DSize = 16384;

    /// <summary>D3D11's subresource numbering: mips of slice 0, then of slice 1, and so on.</summary>
    public static int CalculateSubResourceIndex(int mipSlice, int arraySlice, int mipLevels) => mipSlice + arraySlice * mipLevels;

    /// <summary>
    /// Claims a share of this resource's lifetime. A D3D11 view AddRefs what it points at, and operators rely
    /// on that: they dispose a texture while views of it are still bound, and expect the texture to outlive
    /// them. Without it the memory is freed while a descriptor still references the image.
    /// </summary>
    internal void AddReference() => Interlocked.Increment(ref _references);

    /// <summary>
    /// Gives up the owner's share. Only the first call counts: SharpDX made a second Dispose a no-op, operator
    /// code relies on that, and counting it again would free a resource that views still point at.
    /// </summary>
    public override void Dispose()
    {
        if (!MarkDisposed())
            return;

        ReleaseReference();
    }

    /// <summary>True the first time it is called on this object, false after.</summary>
    protected bool MarkDisposed() => Interlocked.Exchange(ref _disposeCalled, 1) == 0;

    /// <summary>Gives up one share of the lifetime; the last one frees the native object.</summary>
    internal void ReleaseReference()
    {
        if (Interlocked.Decrement(ref _references) > 0)
            return;

        Native?.Dispose();
        GC.SuppressFinalize(this);
    }

    private int _references = 1;
    private int _disposeCalled;
    private static ulong _nextId;
}

public abstract class Texture : Resource
{
    protected Texture(Device device, GpuTexture? texture) : base(device)
    {
        GpuTexture = texture;
    }

    public GpuTexture? GpuTexture { get; }

    public override GpuResource? Native => GpuTexture;
}

public sealed class Texture1D : Texture
{
    public Texture1D(Device device, Texture1DDescription description)
        : this(device, description, ReadOnlySpan<byte>.Empty)
    {
    }

    public Texture1D(Device device, Texture1DDescription description, ReadOnlySpan<byte> initialData)
        : base(device, device.Backend.CreateTexture(new TextureDescription
                                                        {
                                                            Dimension = TextureDimension.Texture1D,
                                                            Width = description.Width,
                                                            Height = 1,
                                                            Depth = 1,
                                                            ArraySize = Math.Max(1, description.ArraySize),
                                                            MipLevels = Math.Max(1, description.MipLevels),
                                                            Format = description.Format,
                                                            Samples = new SampleDescription(1, 0),
                                                            Usage = Translate.ToTextureUsage(description.BindFlags, description.Usage,
                                                                                             description.CpuAccessFlags, description.OptionFlags),
                                                            Memory = Translate.ToMemoryKind(description.Usage, description.CpuAccessFlags),
                                                        },
                                                    initialData))
    {
        Description = description;
    }

    public Texture1D(Device device, Texture1DDescription description, DataBox[] data)
        : base(device, device.Backend.CreateTexture(new TextureDescription
                                                        {
                                                            Dimension = TextureDimension.Texture1D,
                                                            Width = description.Width,
                                                            Height = 1,
                                                            Depth = 1,
                                                            ArraySize = Math.Max(1, description.ArraySize),
                                                            MipLevels = Math.Max(1, description.MipLevels),
                                                            Format = description.Format,
                                                            Samples = new SampleDescription(1, 0),
                                                            Usage = Translate.ToTextureUsage(description.BindFlags, description.Usage,
                                                                                             description.CpuAccessFlags, description.OptionFlags),
                                                            Memory = Translate.ToMemoryKind(description.Usage, description.CpuAccessFlags),
                                                        },
                                                    Texture2D.ToSubresources(data)))
    {
        Description = description;
    }

    public Texture1DDescription Description { get; }
}

public sealed class Texture2D : Texture
{
    public Texture2D(Device device, Texture2DDescription description)
        : this(device, description, ReadOnlySpan<byte>.Empty)
    {
    }

    public Texture2D(Device device, Texture2DDescription description, ReadOnlySpan<byte> initialData)
        : base(device, device.Backend.CreateTexture(new TextureDescription
                                                        {
                                                            Dimension = (description.OptionFlags & ResourceOptionFlags.TextureCube) != 0
                                                                            ? TextureDimension.TextureCube
                                                                            : TextureDimension.Texture2D,
                                                            Width = description.Width,
                                                            Height = description.Height,
                                                            Depth = 1,
                                                            ArraySize = Math.Max(1, description.ArraySize),
                                                            MipLevels = Math.Max(1, description.MipLevels),
                                                            Format = description.Format,
                                                            Samples = description.SampleDescription.Count == 0
                                                                          ? new SampleDescription(1, 0)
                                                                          : description.SampleDescription,
                                                            Usage = Translate.ToTextureUsage(description.BindFlags, description.Usage,
                                                                                             description.CpuAccessFlags, description.OptionFlags),
                                                            Memory = Translate.ToMemoryKind(description.Usage, description.CpuAccessFlags),
                                                        },
                                                    initialData))
    {
        Description = description;
    }

    /// <summary>One entry per mip and slice, in D3D11's order.</summary>
    public Texture2D(Device device, Texture2DDescription description, DataRectangle[] data)
        : base(device, device.Backend.CreateTexture(Describe(description), ToSubresources(data, description)))
    {
        Description = description;
    }

    public Texture2D(Device device, Texture2DDescription description, DataBox[] data)
        : base(device, device.Backend.CreateTexture(Describe(description), ToSubresources(data)))
    {
        Description = description;
    }

    internal static TextureDescription Describe(in Texture2DDescription description)
        => new()
               {
                   Dimension = (description.OptionFlags & ResourceOptionFlags.TextureCube) != 0
                                   ? TextureDimension.TextureCube
                                   : TextureDimension.Texture2D,
                   Width = description.Width,
                   Height = description.Height,
                   Depth = 1,
                   ArraySize = Math.Max(1, description.ArraySize),
                   MipLevels = Math.Max(1, description.MipLevels),
                   Format = description.Format,
                   Samples = description.SampleDescription.Count == 0 ? new SampleDescription(1, 0) : description.SampleDescription,
                   Usage = Translate.ToTextureUsage(description.BindFlags, description.Usage, description.CpuAccessFlags, description.OptionFlags),
                   Memory = Translate.ToMemoryKind(description.Usage, description.CpuAccessFlags),
               };

    /// <summary>
    /// A D3D11 <see cref="DataRectangle"/> carries only a row pitch, but the backends need to know how many
    /// bytes a subresource holds. Derive it from the mip's height: staging only the row pitch makes the copy
    /// read past the end of the buffer, which faults the GPU rather than failing cleanly.
    /// </summary>
    internal static SubresourceData[] ToSubresources(DataRectangle[] data, in Texture2DDescription description)
    {
        // A render target is created through this overload with no pixels at all.
        if (data == null || data.Length == 0)
            return [];

        var result = new SubresourceData[data.Length];
        var mipLevels = Math.Max(1, description.MipLevels);

        for (var i = 0; i < data.Length; i++)
        {
            var rows = Math.Max(1, description.Height >> (i % mipLevels));
            result[i] = new SubresourceData(data[i].DataPointer, data[i].Pitch, data[i].Pitch * rows);
        }

        return result;
    }

    internal static SubresourceData[] ToSubresources(DataBox[] data)
    {
        var result = new SubresourceData[data.Length];

        for (var i = 0; i < data.Length; i++)
        {
            result[i] = new SubresourceData(data[i].DataPointer, data[i].RowPitch, data[i].SlicePitch);
        }

        return result;
    }

    /// <summary>
    /// Wraps a texture the backend already owns: swapchain back buffers, and later the imported surfaces
    /// FFmpeg and Spout hand over.
    /// </summary>
    internal Texture2D(Device device, GpuTexture texture, Texture2DDescription description) : base(device, texture)
    {
        Description = description;
    }

    public Texture2DDescription Description { get; }
}

public sealed class Texture3D : Texture
{
    public Texture3D(Device device, Texture3DDescription description)
        : this(device, description, ReadOnlySpan<byte>.Empty)
    {
    }

    public Texture3D(Device device, Texture3DDescription description, ReadOnlySpan<byte> initialData)
        : base(device, device.Backend.CreateTexture(new TextureDescription
                                                        {
                                                            Dimension = TextureDimension.Texture3D,
                                                            Width = description.Width,
                                                            Height = description.Height,
                                                            Depth = description.Depth,
                                                            ArraySize = 1,
                                                            MipLevels = Math.Max(1, description.MipLevels),
                                                            Format = description.Format,
                                                            Samples = new SampleDescription(1, 0),
                                                            Usage = Translate.ToTextureUsage(description.BindFlags, description.Usage,
                                                                                             description.CpuAccessFlags, description.OptionFlags),
                                                            Memory = Translate.ToMemoryKind(description.Usage, description.CpuAccessFlags),
                                                        },
                                                    initialData))
    {
        Description = description;
    }

    public Texture3D(Device device, Texture3DDescription description, DataBox[] data)
        : base(device, device.Backend.CreateTexture(new TextureDescription
                                                        {
                                                            Dimension = TextureDimension.Texture3D,
                                                            Width = description.Width,
                                                            Height = description.Height,
                                                            Depth = description.Depth,
                                                            ArraySize = 1,
                                                            MipLevels = Math.Max(1, description.MipLevels),
                                                            Format = description.Format,
                                                            Samples = new SampleDescription(1, 0),
                                                            Usage = Translate.ToTextureUsage(description.BindFlags, description.Usage,
                                                                                             description.CpuAccessFlags, description.OptionFlags),
                                                            Memory = Translate.ToMemoryKind(description.Usage, description.CpuAccessFlags),
                                                        },
                                                    Texture2D.ToSubresources(data)))
    {
        Description = description;
    }

    public Texture3DDescription Description { get; }
}

public sealed class Buffer : Resource
{
    public Buffer(Device device, BufferDescription description)
        : this(device, description, ReadOnlySpan<byte>.Empty)
    {
    }

    public Buffer(Device device, ref BufferDescription description)
        : this(device, description, ReadOnlySpan<byte>.Empty)
    {
    }

    /// <summary>The argument order matches SharpDX's seven-argument constructor, which TiXL uses in 23 places.</summary>
    public Buffer(Device device, int sizeInBytes, ResourceUsage usage, BindFlags bindFlags, CpuAccessFlags cpuAccessFlags,
                  ResourceOptionFlags optionFlags, int structureByteStride)
        : this(device,
               new BufferDescription
                   {
                       SizeInBytes = sizeInBytes,
                       Usage = usage,
                       BindFlags = bindFlags,
                       CpuAccessFlags = cpuAccessFlags,
                       OptionFlags = optionFlags,
                       StructureByteStride = structureByteStride,
                   },
               ReadOnlySpan<byte>.Empty)
    {
    }

    public unsafe Buffer(Device device, DataStream data, BufferDescription description)
        : this(device, description, new ReadOnlySpan<byte>((void*)data.DataPointer, description.SizeInBytes))
    {
    }

    public Buffer(Device device, BufferDescription description, ReadOnlySpan<byte> initialData) : base(device)
    {
        Description = description;
        GpuBuffer = device.Backend.CreateBuffer(new GpuBufferDescription
                                                    {
                                                        SizeInBytes = description.SizeInBytes,
                                                        StructureByteStride = description.StructureByteStride,
                                                        Usage = Translate.ToBufferUsage(description.BindFlags, description.OptionFlags),
                                                        Memory = Translate.ToMemoryKind(description.Usage, description.CpuAccessFlags),
                                                    },
                                                initialData);
    }

    public BufferDescription Description { get; }
    public GpuBuffer? GpuBuffer { get; }
    public override GpuResource? Native => GpuBuffer;
}

public abstract class ResourceView : Resource
{
    protected ResourceView(Device device, Resource resource) : base(device)
    {
        Resource = resource;
        resource.AddReference();
    }

    /// <summary>The resource this view points at. Operators read it back to compare identity.</summary>
    public Resource Resource { get; }

    /// <summary>Gives up the share of the resource's lifetime this view claimed. Every view's Dispose calls it.</summary>
    protected void ReleaseResource() => Resource.ReleaseReference();
}

public sealed class ShaderResourceView : ResourceView
{
    /// <summary>The default view: the whole resource, in its own format.</summary>
    public ShaderResourceView(Device device, Resource resource)
        : this(device, resource, ViewDescriptions.DefaultShaderResourceView(resource))
    {
    }

    public ShaderResourceView(Device device, Resource resource, ShaderResourceViewDescription description) : base(device, resource)
    {
        Description = description;

        if (resource is Texture { GpuTexture: { } texture })
            GpuView = device.Backend.CreateTextureView(texture, ViewDescriptions.ToTextureView(description));

        if (GpuView != null)
        {
            lock (_byImGuiId)
            {
                _byImGuiId[GpuView.ImGuiTextureId] = new WeakReference<ShaderResourceView>(this);
            }
        }
    }

    /// <summary>
    /// Finds the view an ImGui draw command refers to. The draw list is consumed a frame after it was built,
    /// so a view disposed in between resolves to null and the command draws nothing — which is what D3D11 got
    /// away with by handing ImGui a raw pointer.
    /// </summary>
    public static ShaderResourceView? FromImGuiTextureId(ulong id)
    {
        if (id == 0)
            return null;

        lock (_byImGuiId)
        {
            if (!_byImGuiId.TryGetValue(id, out var weak) || !weak.TryGetTarget(out var view))
                return null;

            return view.IsDisposed ? null : view;
        }
    }

    private static readonly Dictionary<ulong, WeakReference<ShaderResourceView>> _byImGuiId = [];

    public ShaderResourceViewDescription Description { get; }

    /// <summary>Null for buffer views: those bind the buffer itself with a range.</summary>
    public GpuTextureView? GpuView { get; }

    /// <summary>
    /// What <c>ImGui.Image</c> takes, replacing the raw native pointer. The renderer resolves it when the
    /// draw list is consumed, so a view disposed in between draws nothing instead of crashing.
    /// </summary>
    public ulong ImGuiTextureId => GpuView?.ImGuiTextureId ?? 0;

    public override GpuResource? Native => GpuView ?? (GpuResource?)(Resource as Buffer)?.GpuBuffer;

    public override void Dispose()
    {
        if (!MarkDisposed())
            return;

        if (GpuView != null)
        {
            lock (_byImGuiId)
            {
                _byImGuiId.Remove(GpuView.ImGuiTextureId);
            }
        }

        // A buffer view owns nothing of its own; disposing it must not take the buffer with it.
        GpuView?.Dispose();
        ReleaseResource();
        GC.SuppressFinalize(this);
    }
}

public sealed class RenderTargetView : ResourceView
{
    public RenderTargetView(Device device, Resource resource)
        : this(device, resource, ViewDescriptions.DefaultRenderTargetView(resource))
    {
    }

    public RenderTargetView(Device device, Resource resource, RenderTargetViewDescription description) : base(device, resource)
    {
        Description = description;

        if (resource is Texture { GpuTexture: { } texture })
            GpuView = device.Backend.CreateTextureView(texture, ViewDescriptions.ToTextureView(description));
    }

    public RenderTargetViewDescription Description { get; }
    public GpuTextureView? GpuView { get; }
    public override GpuResource? Native => GpuView;

    public override void Dispose()
    {
        if (!MarkDisposed())
            return;

        GpuView?.Dispose();
        ReleaseResource();
        GC.SuppressFinalize(this);
    }
}

public sealed class DepthStencilView : ResourceView
{
    public DepthStencilView(Device device, Resource resource)
        : this(device, resource, ViewDescriptions.DefaultDepthStencilView(resource))
    {
    }

    public DepthStencilView(Device device, Resource resource, DepthStencilViewDescription description) : base(device, resource)
    {
        Description = description;

        if (resource is Texture { GpuTexture: { } texture })
            GpuView = device.Backend.CreateTextureView(texture, ViewDescriptions.ToTextureView(description));
    }

    public DepthStencilViewDescription Description { get; }
    public GpuTextureView? GpuView { get; }
    public override GpuResource? Native => GpuView;

    public override void Dispose()
    {
        if (!MarkDisposed())
            return;

        GpuView?.Dispose();
        ReleaseResource();
        GC.SuppressFinalize(this);
    }
}

public sealed class UnorderedAccessView : ResourceView
{
    public UnorderedAccessView(Device device, Resource resource)
        : this(device, resource, ViewDescriptions.DefaultUnorderedAccessView(resource))
    {
    }

    public UnorderedAccessView(Device device, Resource resource, UnorderedAccessViewDescription description) : base(device, resource)
    {
        Description = description;

        if (resource is Texture { GpuTexture: { } texture })
            GpuView = device.Backend.CreateTextureView(texture, ViewDescriptions.ToTextureView(description));
    }

    public UnorderedAccessViewDescription Description { get; }
    public GpuTextureView? GpuView { get; }
    public override GpuResource? Native => GpuView ?? (GpuResource?)(Resource as Buffer)?.GpuBuffer;

    public override void Dispose()
    {
        if (!MarkDisposed())
            return;

        GpuView?.Dispose();
        ReleaseResource();
        GC.SuppressFinalize(this);
    }
}

// State objects. D3D11 creates them up front and binds them later; the backend bakes them into a pipeline at
// draw time, so these only carry the translated state. The Gfx operators recreate them whenever an input
// changes, which stays cheap: nothing is allocated on the GPU here.

public sealed class SamplerState : Resource
{
    public SamplerState(Device device, SamplerStateDescription description) : base(device)
    {
        Description = description;
        GpuSampler = device.Backend.CreateSampler(Translate.ToSampler(description));
    }

    public SamplerStateDescription Description { get; }
    public GpuSampler? GpuSampler { get; }
    public override GpuResource? Native => GpuSampler;
}

public sealed class RasterizerState(Device device, RasterizerStateDescription description) : Resource(device)
{
    public RasterizerStateDescription Description { get; } = description;
    internal readonly T3.Graphics.RasterState State = Translate.ToRasterizer(description);
    public override GpuResource? Native => null;
    public override void Dispose() => GC.SuppressFinalize(this);
}

public sealed class DepthStencilState(Device device, DepthStencilStateDescription description) : Resource(device)
{
    public DepthStencilStateDescription Description { get; } = description;
    internal readonly T3.Graphics.DepthState State = Translate.ToDepthStencil(description);
    public override GpuResource? Native => null;
    public override void Dispose() => GC.SuppressFinalize(this);
}

public sealed class BlendState : Resource
{
    public BlendState(Device device, BlendStateDescription description) : base(device)
    {
        Description = description;
        AlphaToCoverage = description.AlphaToCoverageEnable;

        var targets = description.RenderTarget;
        for (var i = 0; i < BlendTargetStates.MaxRenderTargets; i++)
        {
            // D3D11 uses slot 0 for every target unless independent blending is on.
            var source = !description.IndependentBlendEnable || i >= targets.Length ? 0 : i;
            States[i] = Translate.ToBlend(targets[source]);
        }
    }

    public BlendStateDescription Description { get; }
    internal readonly bool AlphaToCoverage;
    internal BlendTargetStates States;
    public override GpuResource? Native => null;
    public override void Dispose() => GC.SuppressFinalize(this);
}

// Shaders. Operators hold TiXL's own wrapper types and never create these; the shader compiler does.

public abstract class Shader(Device device, GpuShader? shader) : Resource(device)
{
    public GpuShader? GpuShader { get; } = shader;
    public override GpuResource? Native => GpuShader;
}

public sealed class VertexShader : Shader
{
    public VertexShader(Device device, GpuShader? shader) : base(device, shader)
    {
    }

    /// <summary>
    /// From compiled bytecode, the way the D3D11 shader compiler creates one. The third argument is D3D11's
    /// class linkage, which TiXL always passes as null.
    /// </summary>
    public VertexShader(Device device, byte[] bytecode, object? classLinkage = null)
        : base(device, device.Backend.CreateShader(ShaderStage.Vertex, bytecode, "main"))
    {
    }
}

public sealed class PixelShader : Shader
{
    public PixelShader(Device device, GpuShader? shader) : base(device, shader)
    {
    }

    public PixelShader(Device device, byte[] bytecode, object? classLinkage = null)
        : base(device, device.Backend.CreateShader(ShaderStage.Pixel, bytecode, "main"))
    {
    }
}

public sealed class GeometryShader : Shader
{
    public GeometryShader(Device device, GpuShader? shader) : base(device, shader)
    {
    }

    public GeometryShader(Device device, byte[] bytecode, object? classLinkage = null)
        : base(device, device.Backend.CreateShader(ShaderStage.Geometry, bytecode, "main"))
    {
    }
}

public sealed class ComputeShader : Shader
{
    public ComputeShader(Device device, GpuShader? shader) : base(device, shader)
    {
    }

    public ComputeShader(Device device, byte[] bytecode, object? classLinkage = null)
        : base(device, device.Backend.CreateShader(ShaderStage.Compute, bytecode, "main"))
    {
    }
}

/// <summary>
/// The vertex layout. D3D11 validates it against the vertex shader's signature at creation; the backend
/// resolves the semantics when it builds the pipeline, so this only carries the elements.
/// </summary>
public sealed class InputLayout : Resource
{
    public InputLayout(Device device, VertexAttribute[] elements) : base(device)
    {
        Elements = elements;
    }

    /// <summary>
    /// The D3D11 shape. The shader bytecode it validated against is not needed here: the backend matches the
    /// semantics against the shader's own reflection when it builds the pipeline.
    /// </summary>
    public InputLayout(Device device, byte[] shaderBytecode, InputElement[] elements) : base(device)
    {
        Elements = new VertexAttribute[elements.Length];

        for (var i = 0; i < elements.Length; i++)
        {
            Elements[i] = elements[i].ToAttribute();
        }
    }

    internal readonly VertexAttribute[] Elements;
    public override GpuResource? Native => null;
    public override void Dispose() => GC.SuppressFinalize(this);
}

public readonly record struct VertexBufferBinding(Buffer? Buffer, int Stride, int Offset);

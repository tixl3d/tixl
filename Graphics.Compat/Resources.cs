using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// Base for the D3D11-shaped resources. Everything here is a thin wrapper around a backend handle: the
/// wrapper carries the D3D11 description operators read back, the handle does the work.
/// </summary>
public abstract class Resource : IDisposable
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

    public string? DebugName
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

    public virtual void Dispose()
    {
        Native?.Dispose();
        GC.SuppressFinalize(this);
    }

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

    public Texture3DDescription Description { get; }
}

public sealed class Buffer : Resource
{
    public Buffer(Device device, BufferDescription description)
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

public abstract class ResourceView(Device device, Resource resource) : Resource(device)
{
    /// <summary>The resource this view points at. Operators read it back to compare identity.</summary>
    public Resource ViewedResource { get; } = resource;
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
    }

    public ShaderResourceViewDescription Description { get; }

    /// <summary>Null for buffer views: those bind the buffer itself with a range.</summary>
    public GpuTextureView? GpuView { get; }

    /// <summary>
    /// What <c>ImGui.Image</c> takes, replacing the raw native pointer. The renderer resolves it when the
    /// draw list is consumed, so a view disposed in between draws nothing instead of crashing.
    /// </summary>
    public ulong ImGuiTextureId => GpuView?.ImGuiTextureId ?? 0;

    public override GpuResource? Native => GpuView ?? (GpuResource?)(ViewedResource as Buffer)?.GpuBuffer;

    public override void Dispose()
    {
        // A buffer view owns nothing of its own; disposing it must not take the buffer with it.
        GpuView?.Dispose();
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
    public override GpuResource? Native => GpuView ?? (GpuResource?)(ViewedResource as Buffer)?.GpuBuffer;

    public override void Dispose()
    {
        GpuView?.Dispose();
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

public sealed class VertexShader(Device device, GpuShader? shader) : Shader(device, shader);

public sealed class PixelShader(Device device, GpuShader? shader) : Shader(device, shader);

public sealed class GeometryShader(Device device, GpuShader? shader) : Shader(device, shader);

public sealed class ComputeShader(Device device, GpuShader? shader) : Shader(device, shader);

/// <summary>
/// The vertex layout. D3D11 validates it against the vertex shader's signature at creation; the backend
/// resolves the semantics when it builds the pipeline, so this only carries the elements.
/// </summary>
public sealed class InputLayout(Device device, VertexAttribute[] elements) : Resource(device)
{
    internal readonly VertexAttribute[] Elements = elements;
    public override GpuResource? Native => null;
    public override void Dispose() => GC.SuppressFinalize(this);
}

public readonly record struct VertexBufferBinding(Buffer? Buffer, int Stride, int Offset);

using SharpDX.Direct3D11;
using T3.Graphics;
using D3D = SharpDX.Direct3D;
using D3D11Device = SharpDX.Direct3D11.Device;

namespace T3.Graphics.D3D11;

/// <summary>
/// Releases immediately. The backend API promises deferred destruction, but on D3D11 the driver already
/// tracks resources against the work that references them, and today's operators dispose and recreate views
/// mid-frame on exactly this behaviour. The Vulkan backend is where the retirement queue has to be real.
/// </summary>
internal sealed class D3D11Texture(D3D11Device device, Resource resource, TextureDescription description, string? label)
    : GpuTexture(description, label)
{
    internal readonly Resource Resource = resource;
    internal readonly D3D11Device Device = device;

    public override IntPtr NativeHandle => Resource.NativePointer;

    /// <summary>False for a texture another library owns, which this only wraps.</summary>
    internal bool OwnsResource = true;

    protected override void ReleaseWhenRetired()
    {
        if (OwnsResource)
            Resource.Dispose();
    }
}

/// <summary>
/// Vulkan has one image view per use; D3D11 has four different objects. The view therefore creates the D3D11
/// views it turns out to need, and only those — a sampled-only texture never allocates a render target view.
/// </summary>
internal sealed class D3D11TextureView(D3D11Texture texture, TextureViewDescription description, string? label)
    : GpuTextureView(texture, description, label)
{
    internal ShaderResourceView? ShaderResource => _shaderResource ??= Create(() => new ShaderResourceView(texture.Device, texture.Resource,
                                                                                                          ShaderResourceDescription()));

    internal RenderTargetView? RenderTarget => _renderTarget ??= Create(() => new RenderTargetView(texture.Device, texture.Resource,
                                                                                                  RenderTargetDescription()));

    internal DepthStencilView? DepthStencil => _depthStencil ??= Create(() => new DepthStencilView(texture.Device, texture.Resource,
                                                                                                  DepthStencilDescription()));

    internal UnorderedAccessView? UnorderedAccess => _unorderedAccess ??= Create(() => new UnorderedAccessView(texture.Device, texture.Resource,
                                                                                                              UnorderedAccessDescription()));

    public override ulong ImGuiTextureId => (ulong)(ShaderResource?.NativePointer ?? IntPtr.Zero);

    private ShaderResourceViewDescription ShaderResourceDescription()
    {
        var description = new ShaderResourceViewDescription { Format = Convert.ToDxgi(Description.Format) };
        var mipCount = MipCount();

        switch (Description.Dimension)
        {
            case TextureDimension.Texture1D:
                description.Dimension = D3D.ShaderResourceViewDimension.Texture1D;
                description.Texture1D = new ShaderResourceViewDescription.Texture1DResource
                                            { MostDetailedMip = Description.FirstMip, MipLevels = mipCount };
                break;

            case TextureDimension.Texture3D:
                description.Dimension = D3D.ShaderResourceViewDimension.Texture3D;
                description.Texture3D = new ShaderResourceViewDescription.Texture3DResource
                                            { MostDetailedMip = Description.FirstMip, MipLevels = mipCount };
                break;

            case TextureDimension.TextureCube:
                description.Dimension = D3D.ShaderResourceViewDimension.TextureCube;
                description.TextureCube = new ShaderResourceViewDescription.TextureCubeResource
                                              { MostDetailedMip = Description.FirstMip, MipLevels = mipCount };
                break;

            default:
                if (Description.ArraySize > 1)
                {
                    description.Dimension = D3D.ShaderResourceViewDimension.Texture2DArray;
                    description.Texture2DArray = new ShaderResourceViewDescription.Texture2DArrayResource
                                                     {
                                                         MostDetailedMip = Description.FirstMip,
                                                         MipLevels = mipCount,
                                                         FirstArraySlice = Description.FirstArraySlice,
                                                         ArraySize = Description.ArraySize,
                                                     };
                }
                else
                {
                    description.Dimension = D3D.ShaderResourceViewDimension.Texture2D;
                    description.Texture2D = new ShaderResourceViewDescription.Texture2DResource
                                                { MostDetailedMip = Description.FirstMip, MipLevels = mipCount };
                }

                break;
        }

        return description;
    }

    private RenderTargetViewDescription RenderTargetDescription()
    {
        var description = new RenderTargetViewDescription { Format = Convert.ToDxgi(Description.Format) };

        switch (Description.Dimension)
        {
            case TextureDimension.Texture1D:
                description.Dimension = RenderTargetViewDimension.Texture1D;
                description.Texture1D = new RenderTargetViewDescription.Texture1DResource { MipSlice = Description.FirstMip };
                break;

            case TextureDimension.Texture3D:
                description.Dimension = RenderTargetViewDimension.Texture3D;
                description.Texture3D = new RenderTargetViewDescription.Texture3DResource
                                            {
                                                MipSlice = Description.FirstMip,
                                                FirstDepthSlice = Description.FirstArraySlice,
                                                DepthSliceCount = Description.ArraySize,
                                            };
                break;

            default:
                if (Description.ArraySize > 1 || Description.Dimension == TextureDimension.TextureCube)
                {
                    description.Dimension = RenderTargetViewDimension.Texture2DArray;
                    description.Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource
                                                     {
                                                         MipSlice = Description.FirstMip,
                                                         FirstArraySlice = Description.FirstArraySlice,
                                                         ArraySize = Description.ArraySize,
                                                     };
                }
                else
                {
                    description.Dimension = RenderTargetViewDimension.Texture2D;
                    description.Texture2D = new RenderTargetViewDescription.Texture2DResource { MipSlice = Description.FirstMip };
                }

                break;
        }

        return description;
    }

    private DepthStencilViewDescription DepthStencilDescription()
    {
        var description = new DepthStencilViewDescription { Format = Convert.ToDxgi(Description.Format) };

        if (Description.ArraySize > 1)
        {
            description.Dimension = DepthStencilViewDimension.Texture2DArray;
            description.Texture2DArray = new DepthStencilViewDescription.Texture2DArrayResource
                                             {
                                                 MipSlice = Description.FirstMip,
                                                 FirstArraySlice = Description.FirstArraySlice,
                                                 ArraySize = Description.ArraySize,
                                             };
        }
        else
        {
            description.Dimension = DepthStencilViewDimension.Texture2D;
            description.Texture2D = new DepthStencilViewDescription.Texture2DResource { MipSlice = Description.FirstMip };
        }

        return description;
    }

    private UnorderedAccessViewDescription UnorderedAccessDescription()
    {
        var description = new UnorderedAccessViewDescription { Format = Convert.ToDxgi(Description.Format) };

        switch (Description.Dimension)
        {
            case TextureDimension.Texture1D:
                description.Dimension = UnorderedAccessViewDimension.Texture1D;
                description.Texture1D = new UnorderedAccessViewDescription.Texture1DResource { MipSlice = Description.FirstMip };
                break;

            case TextureDimension.Texture3D:
                description.Dimension = UnorderedAccessViewDimension.Texture3D;
                description.Texture3D = new UnorderedAccessViewDescription.Texture3DResource
                                            {
                                                MipSlice = Description.FirstMip,
                                                FirstWSlice = Description.FirstArraySlice,
                                                WSize = Description.ArraySize,
                                            };
                break;

            default:
                if (Description.ArraySize > 1)
                {
                    description.Dimension = UnorderedAccessViewDimension.Texture2DArray;
                    description.Texture2DArray = new UnorderedAccessViewDescription.Texture2DArrayResource
                                                     {
                                                         MipSlice = Description.FirstMip,
                                                         FirstArraySlice = Description.FirstArraySlice,
                                                         ArraySize = Description.ArraySize,
                                                     };
                }
                else
                {
                    description.Dimension = UnorderedAccessViewDimension.Texture2D;
                    description.Texture2D = new UnorderedAccessViewDescription.Texture2DResource { MipSlice = Description.FirstMip };
                }

                break;
        }

        return description;
    }

    /// <summary>A count of 0 means "the rest of the mips", which D3D11 spells -1.</summary>
    private int MipCount() => Description.MipCount == 0 ? -1 : Description.MipCount;

    /// <summary>
    /// A view the texture's bind flags do not allow is a null, not an exception: the compatibility layer
    /// binds whatever an operator set, and D3D11 tolerated a mismatch by ignoring it.
    /// </summary>
    private static T? Create<T>(Func<T> create) where T : class
    {
        try
        {
            return create();
        }
        catch (SharpDX.SharpDXException exception)
        {
            GraphicsLog.WarnOnce($"Could not create a {typeof(T).Name}: {exception.Message}");
            return null;
        }
    }

    protected override void ReleaseWhenRetired()
    {
        _shaderResource?.Dispose();
        _renderTarget?.Dispose();
        _depthStencil?.Dispose();
        _unorderedAccess?.Dispose();
    }

    private ShaderResourceView? _shaderResource;
    private RenderTargetView? _renderTarget;
    private DepthStencilView? _depthStencil;
    private UnorderedAccessView? _unorderedAccess;
}

internal sealed class D3D11Buffer(D3D11Device device, SharpDX.Direct3D11.Buffer buffer, GpuBufferDescription description, string? label)
    : GpuBuffer(description, label)
{
    internal readonly SharpDX.Direct3D11.Buffer Buffer = buffer;

    public override IntPtr NativeHandle => Buffer.NativePointer;

    /// <summary>
    /// Buffer views are not objects in the backend API — a binding names a byte range — so the D3D11 backend
    /// caches one view per range it is asked for.
    /// </summary>
    internal ShaderResourceView? ShaderResourceFor(int offset, int size)
    {
        var key = (offset, size);
        if (_shaderResources.TryGetValue(key, out var view))
            return view;

        var (firstElement, elementCount) = Elements(offset, size);
        var description = new ShaderResourceViewDescription
                              {
                                  Format = Description.StructureByteStride > 0 ? SharpDX.DXGI.Format.Unknown : SharpDX.DXGI.Format.R32_Float,
                                  Dimension = D3D.ShaderResourceViewDimension.ExtendedBuffer,
                                  BufferEx = new ShaderResourceViewDescription.ExtendedBufferResource
                                                 { FirstElement = firstElement, ElementCount = elementCount },
                              };

        view = new ShaderResourceView(device, Buffer, description);
        _shaderResources[key] = view;
        return view;
    }

    internal UnorderedAccessView? UnorderedAccessFor(int offset, int size)
    {
        var key = (offset, size);
        if (_unorderedAccessViews.TryGetValue(key, out var view))
            return view;

        var (firstElement, elementCount) = Elements(offset, size);
        var description = new UnorderedAccessViewDescription
                              {
                                  Format = Description.StructureByteStride > 0 ? SharpDX.DXGI.Format.Unknown : SharpDX.DXGI.Format.R32_Typeless,
                                  Dimension = UnorderedAccessViewDimension.Buffer,
                                  Buffer = new UnorderedAccessViewDescription.BufferResource
                                               {
                                                   FirstElement = firstElement,
                                                   ElementCount = elementCount,
                                                   Flags = Description.StructureByteStride > 0
                                                               ? UnorderedAccessViewBufferFlags.None
                                                               : UnorderedAccessViewBufferFlags.Raw,
                                               },
                              };

        view = new UnorderedAccessView(device, Buffer, description);
        _unorderedAccessViews[key] = view;
        return view;
    }

    private (int FirstElement, int ElementCount) Elements(int offset, int size)
    {
        var stride = Description.StructureByteStride > 0 ? Description.StructureByteStride : 4;
        var length = size > 0 ? size : Description.SizeInBytes - offset;
        return (offset / stride, length / stride);
    }

    protected override void ReleaseWhenRetired()
    {
        foreach (var view in _shaderResources.Values)
        {
            view.Dispose();
        }

        foreach (var view in _unorderedAccessViews.Values)
        {
            view.Dispose();
        }

        Buffer.Dispose();
    }

    private readonly Dictionary<(int, int), ShaderResourceView> _shaderResources = [];
    private readonly Dictionary<(int, int), UnorderedAccessView> _unorderedAccessViews = [];
}

internal sealed class D3D11Sampler(SamplerState state, SamplerDescription description, string? label) : GpuSampler(description, label)
{
    internal readonly SamplerState State = state;

    protected override void ReleaseWhenRetired() => State.Dispose();
}

internal sealed class D3D11Shader(DeviceChild shader, ShaderStage stage, string? label) : GpuShader(stage, label)
{
    internal readonly DeviceChild Shader = shader;

    /// <summary>Kept so an input layout can be validated against the vertex shader's signature.</summary>
    internal byte[]? Bytecode;

    protected override void ReleaseWhenRetired() => Shader.Dispose();
}

/// <summary>
/// D3D11 has no pipeline object, so this is the set of state objects a draw needs, created once per distinct
/// description and applied to the context together.
/// </summary>
internal sealed class D3D11Pipeline : GpuPipeline
{
    internal D3D11Pipeline(D3D11Device device, in GraphicsPipelineDescription description, string? label) : base(label)
    {
        Rasterizer = new RasterizerState(device, Convert.ToD3D(description.Rasterizer));
        DepthStencil = new DepthStencilState(device, Convert.ToD3D(description.DepthStencil));
        Blend = new BlendState(device, Convert.ToD3D(description.Blend, description.RenderTargetCount, description.AlphaToCoverage));
        Topology = Convert.ToD3D(description.Topology, description.PatchControlPoints);
        VertexShader = (description.VertexShader as D3D11Shader)?.Shader as VertexShader;
        InputLayout = CreateInputLayout(device, description);
        PixelShader = (description.PixelShader as D3D11Shader)?.Shader as PixelShader;
        GeometryShader = (description.GeometryShader as D3D11Shader)?.Shader as GeometryShader;
    }

    internal D3D11Pipeline(in ComputePipelineDescription description, string? label) : base(label)
    {
        ComputeShader = (description.ComputeShader as D3D11Shader)?.Shader as ComputeShader;
        Topology = D3D.PrimitiveTopology.Undefined;
    }

    internal readonly RasterizerState? Rasterizer;
    internal readonly DepthStencilState? DepthStencil;
    internal readonly BlendState? Blend;
    internal readonly D3D.PrimitiveTopology Topology;
    internal readonly VertexShader? VertexShader;
    internal readonly PixelShader? PixelShader;
    internal readonly GeometryShader? GeometryShader;
    internal readonly ComputeShader? ComputeShader;
    internal readonly InputLayout? InputLayout;

    /// <summary>
    /// D3D11 validates the layout against the vertex shader's signature, so it belongs to the pipeline rather
    /// than to the vertex layout on its own. The semantic is split the way HLSL writes it: POSITION0.
    /// </summary>
    private static InputLayout? CreateInputLayout(D3D11Device device, in GraphicsPipelineDescription description)
    {
        var attributes = description.VertexLayout;
        if (attributes is not { Length: > 0 } || (description.VertexShader as D3D11Shader)?.Bytecode is not { } bytecode)
            return null;

        var elements = new InputElement[attributes.Length];
        for (var i = 0; i < attributes.Length; i++)
        {
            var attribute = attributes[i];
            var (semantic, index) = SplitSemantic(attribute.Semantic);

            elements[i] = new InputElement(semantic, index, Convert.ToDxgi(attribute.Format), attribute.Offset, attribute.Buffer,
                                           attribute.Rate == VertexInputRate.PerInstance
                                               ? InputClassification.PerInstanceData
                                               : InputClassification.PerVertexData,
                                           attribute.Rate == VertexInputRate.PerInstance ? 1 : 0);
        }

        return new InputLayout(device, bytecode, elements);
    }

    private static (string Name, int Index) SplitSemantic(string semantic)
    {
        var digits = 0;
        while (digits < semantic.Length && char.IsDigit(semantic[^(digits + 1)]))
        {
            digits++;
        }

        return digits == 0
                   ? (semantic, 0)
                   : (semantic[..^digits], int.Parse(semantic[^digits..]));
    }

    internal bool IsCompute => ComputeShader != null;

    protected override void ReleaseWhenRetired()
    {
        Rasterizer?.Dispose();
        DepthStencil?.Dispose();
        Blend?.Dispose();
    }
}

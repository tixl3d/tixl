using SharpDX.Direct3D11;
using T3.Graphics;
using D3D = SharpDX.Direct3D;
using DXGI = SharpDX.DXGI;

namespace T3.Graphics.D3D11;

/// <summary>
/// Backend vocabulary back into D3D11's. The compatibility layer translates D3D11 into the backend API and
/// this translates it back, which sounds circular but is not: code written against the backend API — and
/// eventually all of it — goes through this direction only.
/// </summary>
internal static class Convert
{
    /// <summary>
    /// The generated <see cref="Format"/> is a copy of DXGI's with the same values, and a test keeps them
    /// identical, so the conversion is a cast.
    /// </summary>
    internal static DXGI.Format ToDxgi(Format format) => (DXGI.Format)format;

    internal static D3D.PrimitiveTopology ToD3D(Topology topology, int patchControlPoints)
    {
        return topology switch
                   {
                       Topology.PointList     => D3D.PrimitiveTopology.PointList,
                       Topology.LineList      => D3D.PrimitiveTopology.LineList,
                       Topology.LineStrip     => D3D.PrimitiveTopology.LineStrip,
                       Topology.TriangleStrip => D3D.PrimitiveTopology.TriangleStrip,
                       Topology.TriangleFan   => D3D.PrimitiveTopology.Undefined,
                       Topology.PatchList     => D3D.PrimitiveTopology.PatchListWith1ControlPoints + Math.Max(0, patchControlPoints - 1),
                       _                      => D3D.PrimitiveTopology.TriangleList,
                   };
    }

    internal static RasterizerStateDescription ToD3D(in RasterState state)
    {
        return new RasterizerStateDescription
                   {
                       FillMode = state.Fill == PolygonMode.Wireframe ? FillMode.Wireframe : FillMode.Solid,
                       CullMode = state.Cull switch
                                      {
                                          FaceCulling.Front => CullMode.Front,
                                          FaceCulling.Back  => CullMode.Back,
                                          _                 => CullMode.None,
                                      },
                       IsFrontCounterClockwise = state.FrontFaceIsCounterClockwise,
                       DepthBias = state.DepthBias,
                       DepthBiasClamp = state.DepthBiasClamp,
                       SlopeScaledDepthBias = state.SlopeScaledDepthBias,
                       IsDepthClipEnabled = state.DepthClip,
                       IsScissorEnabled = state.Scissor,
                       IsMultisampleEnabled = false,
                       IsAntialiasedLineEnabled = false,
                   };
    }

    internal static DepthStencilStateDescription ToD3D(in DepthState state)
    {
        return new DepthStencilStateDescription
                   {
                       IsDepthEnabled = state.DepthTest,
                       DepthWriteMask = state.DepthWrite ? DepthWriteMask.All : DepthWriteMask.Zero,
                       DepthComparison = ToD3D(state.DepthCompare),
                       IsStencilEnabled = state.StencilTest,
                       StencilReadMask = state.StencilReadMask,
                       StencilWriteMask = state.StencilWriteMask,
                       FrontFace = ToD3D(state.Front),
                       BackFace = ToD3D(state.Back),
                   };
    }

    private static DepthStencilOperationDescription ToD3D(in StencilFaceState face)
    {
        return new DepthStencilOperationDescription
                   {
                       FailOperation = ToD3D(face.Fail),
                       DepthFailOperation = ToD3D(face.DepthFail),
                       PassOperation = ToD3D(face.Pass),
                       Comparison = ToD3D(face.Compare),
                   };
    }

    internal static BlendStateDescription ToD3D(in BlendTargetStates blend, int renderTargetCount, bool alphaToCoverage)
    {
        var description = BlendStateDescription.Default();
        description.AlphaToCoverageEnable = alphaToCoverage;
        description.IndependentBlendEnable = true;

        for (var i = 0; i < BlendTargetStates.MaxRenderTargets; i++)
        {
            // Targets past the bound ones keep D3D11's default, so an unused slot cannot make two otherwise
            // identical states differ.
            var state = i < renderTargetCount ? blend[i] : new BlendTargetState { WriteMask = ColorComponents.All };

            description.RenderTarget[i] = new RenderTargetBlendDescription
                                              {
                                                  IsBlendEnabled = state.Enabled,
                                                  SourceBlend = ToD3D(state.SourceColor),
                                                  DestinationBlend = ToD3D(state.DestinationColor),
                                                  BlendOperation = ToD3D(state.ColorOp),
                                                  SourceAlphaBlend = ToD3D(state.SourceAlpha),
                                                  DestinationAlphaBlend = ToD3D(state.DestinationAlpha),
                                                  AlphaBlendOperation = ToD3D(state.AlphaOp),
                                                  RenderTargetWriteMask = ToD3D(state.WriteMask),
                                              };
        }

        return description;
    }

    internal static SamplerStateDescription ToD3D(in SamplerDescription description)
    {
        return new SamplerStateDescription
                   {
                       Filter = ToFilter(description),
                       AddressU = ToD3D(description.AddressU),
                       AddressV = ToD3D(description.AddressV),
                       AddressW = ToD3D(description.AddressW),
                       MipLodBias = description.MipLodBias,
                       MaximumAnisotropy = Math.Max(1, description.MaxAnisotropy),
                       ComparisonFunction = description.Compare == null ? Comparison.Never : ToD3D(description.Compare.Value),
                       BorderColor = new SharpDX.Mathematics.Interop.RawColor4(description.BorderColor.X, description.BorderColor.Y,
                                                                               description.BorderColor.Z, description.BorderColor.W),
                       MinimumLod = description.MinLod,
                       MaximumLod = description.MaxLod,
                   };
    }

    /// <summary>
    /// Packs the separate filters back into D3D11's bit field: mip bit 0, mag bit 2, min bit 4, anisotropy
    /// 0x40, and the reduction mode in the two bits at 7.
    /// </summary>
    private static Filter ToFilter(in SamplerDescription description)
    {
        var bits = description.MaxAnisotropy > 1 ? 0x55 : 0;

        if (bits == 0)
        {
            if (description.MipFilter == FilterMode.Linear)
                bits |= 0x1;

            if (description.MagFilter == FilterMode.Linear)
                bits |= 0x4;

            if (description.MinFilter == FilterMode.Linear)
                bits |= 0x10;
        }

        var reduction = description.Reduction switch
                            {
                                SamplerReduction.Minimum => 2,
                                SamplerReduction.Maximum => 3,
                                _                        => description.Compare != null ? 1 : 0,
                            };

        return (Filter)(bits | (reduction << 7));
    }

    internal static TextureAddressMode ToD3D(AddressMode mode)
    {
        return mode switch
                   {
                       AddressMode.MirrorRepeat      => TextureAddressMode.Mirror,
                       AddressMode.ClampToEdge       => TextureAddressMode.Clamp,
                       AddressMode.ClampToBorder     => TextureAddressMode.Border,
                       AddressMode.MirrorClampToEdge => TextureAddressMode.MirrorOnce,
                       _                             => TextureAddressMode.Wrap,
                   };
    }

    internal static Comparison ToD3D(CompareFunction function)
    {
        return function switch
                   {
                       CompareFunction.Never        => Comparison.Never,
                       CompareFunction.Less         => Comparison.Less,
                       CompareFunction.Equal        => Comparison.Equal,
                       CompareFunction.LessEqual    => Comparison.LessEqual,
                       CompareFunction.Greater      => Comparison.Greater,
                       CompareFunction.NotEqual     => Comparison.NotEqual,
                       CompareFunction.GreaterEqual => Comparison.GreaterEqual,
                       _                            => Comparison.Always,
                   };
    }

    internal static BlendOption ToD3D(BlendFactor factor)
    {
        return factor switch
                   {
                       BlendFactor.Zero                        => BlendOption.Zero,
                       BlendFactor.SourceColor                 => BlendOption.SourceColor,
                       BlendFactor.InverseSourceColor          => BlendOption.InverseSourceColor,
                       BlendFactor.SourceAlpha                 => BlendOption.SourceAlpha,
                       BlendFactor.InverseSourceAlpha          => BlendOption.InverseSourceAlpha,
                       BlendFactor.DestinationAlpha            => BlendOption.DestinationAlpha,
                       BlendFactor.InverseDestinationAlpha     => BlendOption.InverseDestinationAlpha,
                       BlendFactor.DestinationColor            => BlendOption.DestinationColor,
                       BlendFactor.InverseDestinationColor     => BlendOption.InverseDestinationColor,
                       BlendFactor.SourceAlphaSaturate         => BlendOption.SourceAlphaSaturate,
                       BlendFactor.BlendFactorConstant         => BlendOption.BlendFactor,
                       BlendFactor.InverseBlendFactorConstant  => BlendOption.InverseBlendFactor,
                       BlendFactor.SecondarySourceColor        => BlendOption.SecondarySourceColor,
                       BlendFactor.InverseSecondarySourceColor => BlendOption.InverseSecondarySourceColor,
                       BlendFactor.SecondarySourceAlpha        => BlendOption.SecondarySourceAlpha,
                       BlendFactor.InverseSecondarySourceAlpha => BlendOption.InverseSecondarySourceAlpha,
                       _                                       => BlendOption.One,
                   };
    }

    internal static BlendOperation ToD3D(BlendOp operation)
    {
        return operation switch
                   {
                       BlendOp.Subtract        => BlendOperation.Subtract,
                       BlendOp.ReverseSubtract => BlendOperation.ReverseSubtract,
                       BlendOp.Min             => BlendOperation.Minimum,
                       BlendOp.Max             => BlendOperation.Maximum,
                       _                       => BlendOperation.Add,
                   };
    }

    internal static StencilOperation ToD3D(StencilOp operation)
    {
        return operation switch
                   {
                       StencilOp.Zero           => StencilOperation.Zero,
                       StencilOp.Replace        => StencilOperation.Replace,
                       StencilOp.IncrementClamp => StencilOperation.IncrementAndClamp,
                       StencilOp.DecrementClamp => StencilOperation.DecrementAndClamp,
                       StencilOp.Invert         => StencilOperation.Invert,
                       StencilOp.IncrementWrap  => StencilOperation.Increment,
                       StencilOp.DecrementWrap  => StencilOperation.Decrement,
                       _                        => StencilOperation.Keep,
                   };
    }

    internal static ColorWriteMaskFlags ToD3D(ColorComponents components)
    {
        var mask = (ColorWriteMaskFlags)0;

        if ((components & ColorComponents.Red) != 0)
            mask |= ColorWriteMaskFlags.Red;

        if ((components & ColorComponents.Green) != 0)
            mask |= ColorWriteMaskFlags.Green;

        if ((components & ColorComponents.Blue) != 0)
            mask |= ColorWriteMaskFlags.Blue;

        if ((components & ColorComponents.Alpha) != 0)
            mask |= ColorWriteMaskFlags.Alpha;

        return mask;
    }

    internal static BindFlags ToD3D(TextureUsage usage)
    {
        var flags = BindFlags.None;

        if ((usage & TextureUsage.Sampled) != 0)
            flags |= BindFlags.ShaderResource;

        if ((usage & TextureUsage.RenderTarget) != 0)
            flags |= BindFlags.RenderTarget;

        if ((usage & TextureUsage.DepthStencil) != 0)
            flags |= BindFlags.DepthStencil;

        if ((usage & TextureUsage.Storage) != 0)
            flags |= BindFlags.UnorderedAccess;

        return flags;
    }

    internal static BindFlags ToD3D(BufferUsage usage)
    {
        var flags = BindFlags.None;

        if ((usage & BufferUsage.Constant) != 0)
            flags |= BindFlags.ConstantBuffer;

        if ((usage & BufferUsage.Vertex) != 0)
            flags |= BindFlags.VertexBuffer;

        if ((usage & BufferUsage.Index) != 0)
            flags |= BindFlags.IndexBuffer;

        if ((usage & (BufferUsage.Structured | BufferUsage.Storage)) != 0)
            flags |= BindFlags.ShaderResource;

        if ((usage & BufferUsage.Storage) != 0)
            flags |= BindFlags.UnorderedAccess;

        return flags;
    }

    internal static ResourceUsage ToD3D(MemoryKind memory)
    {
        return memory switch
                   {
                       MemoryKind.Upload   => ResourceUsage.Dynamic,
                       MemoryKind.Readback => ResourceUsage.Staging,
                       _                   => ResourceUsage.Default,
                   };
    }

    internal static CpuAccessFlags ToCpuAccess(MemoryKind memory)
    {
        return memory switch
                   {
                       MemoryKind.Upload   => CpuAccessFlags.Write,
                       MemoryKind.Readback => CpuAccessFlags.Read,
                       _                   => CpuAccessFlags.None,
                   };
    }
}

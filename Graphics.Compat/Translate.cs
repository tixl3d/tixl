using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// Turns D3D11 descriptions into backend descriptions. This is the whole point of the compatibility layer:
/// the translation lives here once instead of in every operator, and it is what gets deleted when an operator
/// moves to the backend API.
/// </summary>
internal static class Translate
{
    internal static TextureUsage ToTextureUsage(BindFlags bind, ResourceUsage usage, CpuAccessFlags cpuAccess, ResourceOptionFlags options)
    {
        var result = TextureUsage.None;

        if ((bind & BindFlags.ShaderResource) != 0)
            result |= TextureUsage.Sampled;

        if ((bind & BindFlags.RenderTarget) != 0)
            result |= TextureUsage.RenderTarget;

        if ((bind & BindFlags.DepthStencil) != 0)
            result |= TextureUsage.DepthStencil;

        if ((bind & BindFlags.UnorderedAccess) != 0)
            result |= TextureUsage.Storage;

        // D3D11 copies between any two resources without asking for permission, and operators do exactly that
        // (CopyResource appears 32 times). Vulkan wants it declared, so every texture gets it.
        result |= TextureUsage.CopySource | TextureUsage.CopyDestination;

        // Mip generation is a render pass per level in the backend.
        if ((options & ResourceOptionFlags.GenerateMipMaps) != 0)
            result |= TextureUsage.RenderTarget | TextureUsage.Sampled;

        if (usage == ResourceUsage.Staging || cpuAccess != CpuAccessFlags.None)
            result &= ~(TextureUsage.RenderTarget | TextureUsage.DepthStencil | TextureUsage.Storage);

        return result;
    }

    internal static BufferUsage ToBufferUsage(BindFlags bind, ResourceOptionFlags options)
    {
        var result = BufferUsage.CopySource | BufferUsage.CopyDestination;

        if ((bind & BindFlags.ConstantBuffer) != 0)
            result |= BufferUsage.Constant;

        if ((bind & BindFlags.VertexBuffer) != 0)
            result |= BufferUsage.Vertex;

        if ((bind & BindFlags.IndexBuffer) != 0)
            result |= BufferUsage.Index;

        if ((bind & BindFlags.ShaderResource) != 0)
            result |= (options & ResourceOptionFlags.BufferStructured) != 0 ? BufferUsage.Structured : BufferUsage.Storage;

        if ((bind & BindFlags.UnorderedAccess) != 0)
            result |= BufferUsage.Storage;

        if ((options & ResourceOptionFlags.DrawIndirectArguments) != 0)
            result |= BufferUsage.Indirect;

        return result;
    }

    internal static MemoryKind ToMemoryKind(ResourceUsage usage, CpuAccessFlags cpuAccess)
    {
        return usage switch
                   {
                       ResourceUsage.Staging => (cpuAccess & CpuAccessFlags.Read) != 0 ? MemoryKind.Readback : MemoryKind.Upload,
                       ResourceUsage.Dynamic => MemoryKind.Upload,
                       _                     => MemoryKind.DeviceLocal,
                   };
    }

    // D3D11_FILTER is a bit field: bit 0 mip, bit 2 mag, bit 4 min, 0x40 anisotropic, 0x80 comparison.
    private const int MipLinearBit = 0x1;
    private const int MagLinearBit = 0x4;
    private const int MinLinearBit = 0x10;
    private const int AnisotropicBits = 0x40;
    private const int ComparisonBit = 0x80;

    internal static SamplerDescription ToSampler(in SamplerStateDescription description)
    {
        var filter = (int)description.Filter;
        var anisotropic = (filter & AnisotropicBits) != 0;

        return new SamplerDescription
                   {
                       MinFilter = anisotropic || (filter & MinLinearBit) != 0 ? FilterMode.Linear : FilterMode.Nearest,
                       MagFilter = anisotropic || (filter & MagLinearBit) != 0 ? FilterMode.Linear : FilterMode.Nearest,
                       MipFilter = anisotropic || (filter & MipLinearBit) != 0 ? FilterMode.Linear : FilterMode.Nearest,
                       AddressU = ToAddressMode(description.AddressU),
                       AddressV = ToAddressMode(description.AddressV),
                       AddressW = ToAddressMode(description.AddressW),
                       MipLodBias = description.MipLodBias,
                       MaxAnisotropy = anisotropic ? description.MaximumAnisotropy : 1,
                       Compare = (filter & ComparisonBit) != 0 ? ToCompare(description.ComparisonFunction) : null,
                       BorderColor = description.BorderColor,
                       MinLod = description.MinimumLod,
                       MaxLod = description.MaximumLod,
                   };
    }

    internal static AddressMode ToAddressMode(TextureAddressMode mode)
    {
        return mode switch
                   {
                       TextureAddressMode.Wrap       => AddressMode.Repeat,
                       TextureAddressMode.Mirror     => AddressMode.MirrorRepeat,
                       TextureAddressMode.Clamp      => AddressMode.ClampToEdge,
                       TextureAddressMode.Border     => AddressMode.ClampToBorder,
                       TextureAddressMode.MirrorOnce => AddressMode.MirrorClampToEdge,
                       _                             => AddressMode.Repeat,
                   };
    }

    internal static CompareFunction ToCompare(Comparison comparison)
    {
        return comparison switch
                   {
                       Comparison.Never        => CompareFunction.Never,
                       Comparison.Less         => CompareFunction.Less,
                       Comparison.Equal        => CompareFunction.Equal,
                       Comparison.LessEqual    => CompareFunction.LessEqual,
                       Comparison.Greater      => CompareFunction.Greater,
                       Comparison.NotEqual     => CompareFunction.NotEqual,
                       Comparison.GreaterEqual => CompareFunction.GreaterEqual,
                       _                       => CompareFunction.Always,
                   };
    }

    internal static T3.Graphics.RasterState ToRasterizer(in RasterizerStateDescription description)
    {
        return new T3.Graphics.RasterState
                   {
                       Fill = description.FillMode == FillMode.Wireframe ? T3.Graphics.PolygonMode.Wireframe : T3.Graphics.PolygonMode.Solid,
                       Cull = description.CullMode switch
                                  {
                                      CullMode.Front => T3.Graphics.FaceCulling.Front,
                                      CullMode.Back  => T3.Graphics.FaceCulling.Back,
                                      _              => T3.Graphics.FaceCulling.None,
                                  },
                       FrontFaceIsCounterClockwise = description.IsFrontCounterClockwise,
                       DepthBias = description.DepthBias,
                       DepthBiasClamp = description.DepthBiasClamp,
                       SlopeScaledDepthBias = description.SlopeScaledDepthBias,
                       DepthClip = description.IsDepthClipEnabled,
                       Scissor = description.IsScissorEnabled,
                   };
    }

    internal static T3.Graphics.DepthState ToDepthStencil(in DepthStencilStateDescription description)
    {
        return new T3.Graphics.DepthState
                   {
                       DepthTest = description.IsDepthEnabled,
                       DepthWrite = description.DepthWriteMask == DepthWriteMask.All,
                       DepthCompare = ToCompare(description.DepthComparison),
                       StencilTest = description.IsStencilEnabled,
                       StencilReadMask = description.StencilReadMask,
                       StencilWriteMask = description.StencilWriteMask,
                       Front = ToStencilFace(description.FrontFace),
                       Back = ToStencilFace(description.BackFace),
                   };
    }

    private static StencilFaceState ToStencilFace(in DepthStencilOperationDescription description)
    {
        return new StencilFaceState
                   {
                       Fail = ToStencilOp(description.FailOperation),
                       DepthFail = ToStencilOp(description.DepthFailOperation),
                       Pass = ToStencilOp(description.PassOperation),
                       Compare = ToCompare(description.Comparison),
                   };
    }

    private static StencilOp ToStencilOp(StencilOperation operation)
    {
        return operation switch
                   {
                       StencilOperation.Zero           => StencilOp.Zero,
                       StencilOperation.Replace        => StencilOp.Replace,
                       StencilOperation.IncrementAndClamp => StencilOp.IncrementClamp,
                       StencilOperation.DecrementAndClamp => StencilOp.DecrementClamp,
                       StencilOperation.Invert         => StencilOp.Invert,
                       StencilOperation.Increment      => StencilOp.IncrementWrap,
                       StencilOperation.Decrement      => StencilOp.DecrementWrap,
                       _                               => StencilOp.Keep,
                   };
    }

    internal static BlendTargetState ToBlend(in RenderTargetBlendDescription description)
    {
        return new BlendTargetState
                   {
                       Enabled = description.IsBlendEnabled,
                       SourceColor = ToBlendFactor(description.SourceBlend),
                       DestinationColor = ToBlendFactor(description.DestinationBlend),
                       ColorOp = ToBlendOp(description.BlendOperation),
                       SourceAlpha = ToBlendFactor(description.SourceAlphaBlend),
                       DestinationAlpha = ToBlendFactor(description.DestinationAlphaBlend),
                       AlphaOp = ToBlendOp(description.AlphaBlendOperation),
                       WriteMask = ToColorComponents(description.RenderTargetWriteMask),
                   };
    }

    private static ColorComponents ToColorComponents(ColorWriteMaskFlags mask)
    {
        var result = ColorComponents.None;

        if ((mask & ColorWriteMaskFlags.Red) != 0)
            result |= ColorComponents.Red;

        if ((mask & ColorWriteMaskFlags.Green) != 0)
            result |= ColorComponents.Green;

        if ((mask & ColorWriteMaskFlags.Blue) != 0)
            result |= ColorComponents.Blue;

        if ((mask & ColorWriteMaskFlags.Alpha) != 0)
            result |= ColorComponents.Alpha;

        return result;
    }

    private static BlendFactor ToBlendFactor(BlendOption option)
    {
        return option switch
                   {
                       BlendOption.Zero                    => BlendFactor.Zero,
                       BlendOption.One                     => BlendFactor.One,
                       BlendOption.SourceColor             => BlendFactor.SourceColor,
                       BlendOption.InverseSourceColor      => BlendFactor.InverseSourceColor,
                       BlendOption.SourceAlpha             => BlendFactor.SourceAlpha,
                       BlendOption.InverseSourceAlpha      => BlendFactor.InverseSourceAlpha,
                       BlendOption.DestinationAlpha        => BlendFactor.DestinationAlpha,
                       BlendOption.InverseDestinationAlpha => BlendFactor.InverseDestinationAlpha,
                       BlendOption.DestinationColor        => BlendFactor.DestinationColor,
                       BlendOption.InverseDestinationColor => BlendFactor.InverseDestinationColor,
                       BlendOption.SourceAlphaSaturate     => BlendFactor.SourceAlphaSaturate,
                       BlendOption.BlendFactor             => BlendFactor.BlendFactorConstant,
                       BlendOption.InverseBlendFactor      => BlendFactor.InverseBlendFactorConstant,
                       BlendOption.SecondarySourceColor    => BlendFactor.SecondarySourceColor,
                       BlendOption.InverseSecondarySourceColor => BlendFactor.InverseSecondarySourceColor,
                       BlendOption.SecondarySourceAlpha    => BlendFactor.SecondarySourceAlpha,
                       BlendOption.InverseSecondarySourceAlpha => BlendFactor.InverseSecondarySourceAlpha,
                       _                                   => BlendFactor.One,
                   };
    }

    private static BlendOp ToBlendOp(BlendOperation operation)
    {
        return operation switch
                   {
                       BlendOperation.Subtract        => BlendOp.Subtract,
                       BlendOperation.ReverseSubtract => BlendOp.ReverseSubtract,
                       BlendOperation.Minimum         => BlendOp.Min,
                       BlendOperation.Maximum         => BlendOp.Max,
                       _                              => BlendOp.Add,
                   };
    }

    internal static T3.Graphics.Topology ToTopology(PrimitiveTopology topology)
    {
        return topology switch
                   {
                       PrimitiveTopology.PointList     => T3.Graphics.Topology.PointList,
                       PrimitiveTopology.LineList      => T3.Graphics.Topology.LineList,
                       PrimitiveTopology.LineStrip     => T3.Graphics.Topology.LineStrip,
                       PrimitiveTopology.TriangleStrip => T3.Graphics.Topology.TriangleStrip,
                       >= PrimitiveTopology.PatchListWith1ControlPoints => T3.Graphics.Topology.PatchList,
                       _                               => T3.Graphics.Topology.TriangleList,
                   };
    }

    /// <summary>Patch lists encode the control point count in the enum value, as D3D11 does.</summary>
    internal static int ToPatchControlPoints(PrimitiveTopology topology)
    {
        return topology >= PrimitiveTopology.PatchListWith1ControlPoints
                   ? topology - PrimitiveTopology.PatchListWith1ControlPoints + 1
                   : 0;
    }
}

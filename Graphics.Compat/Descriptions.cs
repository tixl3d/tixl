using System.Numerics;
using System.Runtime.InteropServices;
using T3.Graphics;

namespace T3.Graphics.Compat;

// Format and SampleDescription come from T3.Graphics: both layers need them.

// Descriptions keep SharpDX's names and fields so operator code migrates by swapping a using line. Two
// deliberate differences: SharpDX's RawBool becomes bool, and RawColor4 becomes Vector4 (see
// Plan_GraphicsFacade.md, decision 3). Fields TiXL never sets keep D3D11's defaults.

public struct Texture1DDescription
{
    public int Width;
    public int MipLevels;
    public int ArraySize;
    public Format Format;
    public ResourceUsage Usage;
    public BindFlags BindFlags;
    public CpuAccessFlags CpuAccessFlags;
    public ResourceOptionFlags OptionFlags;
}

public struct Texture2DDescription
{
    public int Width;
    public int Height;
    public int MipLevels;
    public int ArraySize;
    public Format Format;
    public SampleDescription SampleDescription;
    public ResourceUsage Usage;
    public BindFlags BindFlags;
    public CpuAccessFlags CpuAccessFlags;
    public ResourceOptionFlags OptionFlags;
}

public struct Texture3DDescription
{
    public int Width;
    public int Height;
    public int Depth;
    public int MipLevels;
    public Format Format;
    public ResourceUsage Usage;
    public BindFlags BindFlags;
    public CpuAccessFlags CpuAccessFlags;
    public ResourceOptionFlags OptionFlags;
}

public struct BufferDescription
{
    public int SizeInBytes;
    public ResourceUsage Usage;
    public BindFlags BindFlags;
    public CpuAccessFlags CpuAccessFlags;
    public ResourceOptionFlags OptionFlags;
    public int StructureByteStride;
}

public struct SamplerStateDescription
{
    public Filter Filter;
    public TextureAddressMode AddressU;
    public TextureAddressMode AddressV;
    public TextureAddressMode AddressW;
    public float MipLodBias;
    public int MaximumAnisotropy;
    public Comparison ComparisonFunction;
    public Vector4 BorderColor;
    public float MinimumLod;
    public float MaximumLod;
}

public struct RasterizerStateDescription
{
    public FillMode FillMode;
    public CullMode CullMode;
    public bool IsFrontCounterClockwise;
    public int DepthBias;
    public float DepthBiasClamp;
    public float SlopeScaledDepthBias;
    public bool IsDepthClipEnabled;
    public bool IsScissorEnabled;
    public bool IsMultisampleEnabled;
    public bool IsAntialiasedLineEnabled;
}

public struct RenderTargetBlendDescription(bool isBlendEnabled, BlendOption sourceBlend, BlendOption destinationBlend,
                                           BlendOperation blendOperation, BlendOption sourceAlphaBlend, BlendOption destinationAlphaBlend,
                                           BlendOperation alphaBlendOperation, ColorWriteMaskFlags renderTargetWriteMask)
{
    public RenderTargetBlendDescription() : this(false, BlendOption.One, BlendOption.Zero, BlendOperation.Add, BlendOption.One,
                                                 BlendOption.Zero, BlendOperation.Add, ColorWriteMaskFlags.All)
    {
    }

    public bool IsBlendEnabled = isBlendEnabled;
    public BlendOption SourceBlend = sourceBlend;
    public BlendOption DestinationBlend = destinationBlend;
    public BlendOperation BlendOperation = blendOperation;
    public BlendOption SourceAlphaBlend = sourceAlphaBlend;
    public BlendOption DestinationAlphaBlend = destinationAlphaBlend;
    public BlendOperation AlphaBlendOperation = alphaBlendOperation;
    public ColorWriteMaskFlags RenderTargetWriteMask = renderTargetWriteMask;
}

public struct BlendStateDescription()
{
    public bool AlphaToCoverageEnable;
    public bool IndependentBlendEnable;

    /// <summary>One entry per render target, as in D3D11.</summary>
    public RenderTargetBlendDescription[] RenderTarget = new RenderTargetBlendDescription[MaxRenderTargets];

    public const int MaxRenderTargets = 8;
}

public struct DepthStencilOperationDescription
{
    public StencilOperation FailOperation;
    public StencilOperation DepthFailOperation;
    public StencilOperation PassOperation;
    public Comparison Comparison;
}

public struct DepthStencilStateDescription
{
    public bool IsDepthEnabled;
    public DepthWriteMask DepthWriteMask;
    public Comparison DepthComparison;
    public bool IsStencilEnabled;
    public byte StencilReadMask;
    public byte StencilWriteMask;
    public DepthStencilOperationDescription FrontFace;
    public DepthStencilOperationDescription BackFace;
}

// View descriptions. SharpDX overlaps the per-dimension members in a union and nests their types inside the
// description, and the facade keeps the same names and shape.

/// <summary>
/// Laid out as D3D11's own: the per-dimension members share memory. Operators rely on that — a structured
/// buffer's view is described through <see cref="BufferEx"/>, and its length read back through
/// <see cref="Buffer"/>. As separate fields that read returns zero, and every point, particle and mesh
/// operator draws nothing.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct ShaderResourceViewDescription
{
    [FieldOffset(0)] public Format Format;
    [FieldOffset(4)] public ShaderResourceViewDimension Dimension;
    [FieldOffset(8)] public BufferResource Buffer;
    [FieldOffset(8)] public ExtendedBufferResource BufferEx;
    [FieldOffset(8)] public Texture1DResource Texture1D;
    [FieldOffset(8)] public Texture1DArrayResource Texture1DArray;
    [FieldOffset(8)] public Texture2DResource Texture2D;
    [FieldOffset(8)] public Texture2DArrayResource Texture2DArray;
    [FieldOffset(8)] public Texture3DResource Texture3D;
    [FieldOffset(8)] public TextureCubeResource TextureCube;
    [FieldOffset(8)] public TextureCubeArrayResource TextureCubeArray;

    /// <summary>D3D11_BUFFER_SRV is itself two unions, which is what lines its count up with BufferEx's.</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct BufferResource
    {
        [FieldOffset(0)] public int FirstElement;
        [FieldOffset(0)] public int ElementOffset;
        [FieldOffset(4)] public int ElementCount;
        [FieldOffset(4)] public int ElementWidth;
    }

    public struct ExtendedBufferResource
    {
        public int FirstElement;
        public int ElementCount;
        public ShaderResourceViewExtendedBufferFlags Flags;
    }

    public struct Texture1DResource
    {
        public int MostDetailedMip;
        public int MipLevels;
    }

    public struct Texture1DArrayResource
    {
        public int MostDetailedMip;
        public int MipLevels;
        public int FirstArraySlice;
        public int ArraySize;
    }

    public struct Texture2DResource
    {
        public int MostDetailedMip;
        public int MipLevels;
    }

    public struct Texture2DArrayResource
    {
        public int MostDetailedMip;
        public int MipLevels;
        public int FirstArraySlice;
        public int ArraySize;
    }

    public struct Texture3DResource
    {
        public int MostDetailedMip;
        public int MipLevels;
    }

    public struct TextureCubeResource
    {
        public int MostDetailedMip;
        public int MipLevels;
    }

    public struct TextureCubeArrayResource
    {
        public int MostDetailedMip;
        public int MipLevels;
        public int First2DArrayFace;
        public int CubeCount;
    }
}

public struct RenderTargetViewDescription
{
    public Format Format;
    public RenderTargetViewDimension Dimension;
    public BufferResource Buffer;
    public Texture1DResource Texture1D;
    public Texture1DArrayResource Texture1DArray;
    public Texture2DResource Texture2D;
    public Texture2DArrayResource Texture2DArray;
    public Texture3DResource Texture3D;

    public struct BufferResource
    {
        public int FirstElement;
        public int ElementOffset;
        public int ElementCount;
        public int ElementWidth;
    }

    public struct Texture1DResource
    {
        public int MipSlice;
    }

    public struct Texture1DArrayResource
    {
        public int MipSlice;
        public int FirstArraySlice;
        public int ArraySize;
    }

    public struct Texture2DResource
    {
        public int MipSlice;
    }

    public struct Texture2DArrayResource
    {
        public int MipSlice;
        public int FirstArraySlice;
        public int ArraySize;
    }

    public struct Texture3DResource
    {
        public int MipSlice;
        public int FirstDepthSlice;
        public int DepthSliceCount;
    }
}

public struct DepthStencilViewDescription
{
    public Format Format;
    public DepthStencilViewDimension Dimension;
    public DepthStencilViewFlags Flags;
    public Texture1DResource Texture1D;
    public Texture1DArrayResource Texture1DArray;
    public Texture2DResource Texture2D;
    public Texture2DArrayResource Texture2DArray;

    public struct Texture1DResource
    {
        public int MipSlice;
    }

    public struct Texture1DArrayResource
    {
        public int MipSlice;
        public int FirstArraySlice;
        public int ArraySize;
    }

    public struct Texture2DResource
    {
        public int MipSlice;
    }

    public struct Texture2DArrayResource
    {
        public int MipSlice;
        public int FirstArraySlice;
        public int ArraySize;
    }
}

public struct UnorderedAccessViewDescription
{
    public Format Format;
    public UnorderedAccessViewDimension Dimension;
    public BufferResource Buffer;
    public Texture1DResource Texture1D;
    public Texture1DArrayResource Texture1DArray;
    public Texture2DResource Texture2D;
    public Texture2DArrayResource Texture2DArray;
    public Texture3DResource Texture3D;

    public struct BufferResource
    {
        public int FirstElement;
        public int ElementCount;
        public UnorderedAccessViewBufferFlags Flags;
    }

    public struct Texture1DResource
    {
        public int MipSlice;
    }

    public struct Texture1DArrayResource
    {
        public int MipSlice;
        public int FirstArraySlice;
        public int ArraySize;
    }

    public struct Texture2DResource
    {
        public int MipSlice;
    }

    public struct Texture2DArrayResource
    {
        public int MipSlice;
        public int FirstArraySlice;
        public int ArraySize;
    }

    public struct Texture3DResource
    {
        public int MipSlice;
        public int FirstWSlice;
        public int WSize;
    }
}

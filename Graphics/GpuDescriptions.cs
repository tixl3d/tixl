using System.Numerics;

namespace T3.Graphics;

// Descriptions for the backend API. Deliberately not D3D11's: usage is explicit rather than bind flags, and
// pipelines are objects rather than loose state. The compatibility layer translates D3D11 descriptions into
// these.

[Flags]
public enum TextureUsage
{
    None = 0,
    Sampled = 1,
    RenderTarget = 2,
    DepthStencil = 4,
    Storage = 8,
    CopySource = 16,
    CopyDestination = 32,
}

[Flags]
public enum BufferUsage
{
    None = 0,
    Constant = 1,
    Vertex = 2,
    Index = 4,
    Structured = 8,
    Storage = 16,
    Indirect = 32,
    CopySource = 64,
    CopyDestination = 128,
}

/// <summary>Who writes the bytes, and how often.</summary>
public enum MemoryKind
{
    /// <summary>GPU-only. The default for textures and results.</summary>
    DeviceLocal,

    /// <summary>Written by the CPU every frame through the upload ring: constant buffers, dynamic textures.</summary>
    Upload,

    /// <summary>Read back by the CPU: screenshots, the visual suite, operators that sample the GPU's output.</summary>
    Readback,
}

public enum TextureDimension
{
    Texture1D,
    Texture2D,
    Texture3D,
    TextureCube,
}

public readonly record struct TextureDescription
{
    public TextureDimension Dimension { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Depth { get; init; }
    public int ArraySize { get; init; }
    public int MipLevels { get; init; }
    public Format Format { get; init; }
    public SampleDescription Samples { get; init; }
    public TextureUsage Usage { get; init; }
    public MemoryKind Memory { get; init; }
}

public readonly record struct TextureViewDescription
{
    public Format Format { get; init; }
    public TextureDimension Dimension { get; init; }
    public int FirstMip { get; init; }
    public int MipCount { get; init; }
    public int FirstArraySlice { get; init; }
    public int ArraySize { get; init; }
}

public readonly record struct GpuBufferDescription
{
    public int SizeInBytes { get; init; }
    public int StructureByteStride { get; init; }
    public BufferUsage Usage { get; init; }
    public MemoryKind Memory { get; init; }
}

public readonly record struct SamplerDescription
{
    public FilterMode MinFilter { get; init; }
    public FilterMode MagFilter { get; init; }
    public FilterMode MipFilter { get; init; }
    public AddressMode AddressU { get; init; }
    public AddressMode AddressV { get; init; }
    public AddressMode AddressW { get; init; }
    public float MipLodBias { get; init; }
    public int MaxAnisotropy { get; init; }
    public CompareFunction? Compare { get; init; }

    /// <summary>
    /// D3D11 can reduce a filter footprint by minimum or maximum instead of averaging, and the type picker
    /// offers those filters, so a project can have one saved. Vulkan needs samplerFilterMinmax (core in 1.2).
    /// </summary>
    public SamplerReduction Reduction { get; init; }
    public Vector4 BorderColor { get; init; }
    public float MinLod { get; init; }
    public float MaxLod { get; init; }
}

public enum SamplerReduction
{
    /// <summary>Weighted average, which is what everything but an explicit min/max filter wants.</summary>
    Standard,

    Minimum,
    Maximum,
}

public enum FilterMode
{
    Nearest,
    Linear,
}

public enum AddressMode
{
    Repeat,
    MirrorRepeat,
    ClampToEdge,
    ClampToBorder,
    MirrorClampToEdge,
}

public enum CompareFunction
{
    Never,
    Less,
    Equal,
    LessEqual,
    Greater,
    NotEqual,
    GreaterEqual,
    Always,
}

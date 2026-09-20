namespace T3.Graphics;

// Pipeline state for the backend API. Unlike D3D11's loose state objects this is one immutable description:
// the backend hashes it and hands back a cached GpuPipeline. The compatibility layer assembles a description
// from whatever the operator set on the context and looks it up at draw time.

public enum Topology
{
    PointList,
    LineList,
    LineStrip,
    TriangleList,
    TriangleStrip,

    /// <summary>Kept because projects can pick it; drivers may not support it, so the backend reports that.</summary>
    TriangleFan,

    /// <summary>Control point count comes from the description. Unused by TiXL today, but projects can select it.</summary>
    PatchList,
}

public enum PolygonMode
{
    Solid,
    Wireframe,
}

public enum FaceCulling
{
    None,
    Front,
    Back,
}

public enum BlendFactor
{
    Zero,
    One,
    SourceColor,
    InverseSourceColor,
    SourceAlpha,
    InverseSourceAlpha,
    DestinationAlpha,
    InverseDestinationAlpha,
    DestinationColor,
    InverseDestinationColor,
    SourceAlphaSaturate,
    BlendFactorConstant,
    InverseBlendFactorConstant,
    SecondarySourceColor,
    InverseSecondarySourceColor,
    SecondarySourceAlpha,
    InverseSecondarySourceAlpha,
}

public enum BlendOp
{
    Add,
    Subtract,
    ReverseSubtract,
    Min,
    Max,
}

[Flags]
public enum ColorComponents
{
    None = 0,
    Red = 1,
    Green = 2,
    Blue = 4,
    Alpha = 8,
    All = Red | Green | Blue | Alpha,
}

public enum StencilOp
{
    Keep,
    Zero,
    Replace,
    IncrementClamp,
    DecrementClamp,
    Invert,
    IncrementWrap,
    DecrementWrap,
}

public readonly record struct RasterState
{
    public PolygonMode Fill { get; init; }
    public FaceCulling Cull { get; init; }
    public bool FrontFaceIsCounterClockwise { get; init; }
    public int DepthBias { get; init; }
    public float DepthBiasClamp { get; init; }
    public float SlopeScaledDepthBias { get; init; }
    public bool DepthClip { get; init; }

    /// <summary>Vulkan needs this when the pipeline is built, not when the scissor rect is set.</summary>
    public bool Scissor { get; init; }
}

public readonly record struct StencilFaceState
{
    public StencilOp Fail { get; init; }
    public StencilOp DepthFail { get; init; }
    public StencilOp Pass { get; init; }
    public CompareFunction Compare { get; init; }
}

public readonly record struct DepthState
{
    public bool DepthTest { get; init; }
    public bool DepthWrite { get; init; }
    public CompareFunction DepthCompare { get; init; }
    public bool StencilTest { get; init; }
    public byte StencilReadMask { get; init; }
    public byte StencilWriteMask { get; init; }
    public StencilFaceState Front { get; init; }
    public StencilFaceState Back { get; init; }
}

public readonly record struct BlendTargetState
{
    public bool Enabled { get; init; }
    public BlendFactor SourceColor { get; init; }
    public BlendFactor DestinationColor { get; init; }
    public BlendOp ColorOp { get; init; }
    public BlendFactor SourceAlpha { get; init; }
    public BlendFactor DestinationAlpha { get; init; }
    public BlendOp AlphaOp { get; init; }
    public ColorComponents WriteMask { get; init; }
}

/// <summary>
/// Blend state for up to <see cref="MaxRenderTargets"/> targets. Inline rather than an array so that a
/// pipeline description stays a value and can be hashed per draw without allocating.
/// </summary>
[System.Runtime.CompilerServices.InlineArray(MaxRenderTargets)]
public struct BlendTargetStates
{
    public const int MaxRenderTargets = 8;
    private BlendTargetState _element0;
}

public enum VertexInputRate
{
    PerVertex,
    PerInstance,
}

public readonly record struct VertexAttribute
{
    /// <summary>The HLSL semantic, e.g. POSITION0. The backend resolves it against the shader's reflection.</summary>
    public string Semantic { get; init; }

    public int Buffer { get; init; }
    public int Offset { get; init; }
    public Format Format { get; init; }
    public VertexInputRate Rate { get; init; }
}

public readonly record struct GraphicsPipelineDescription
{
    public GpuShader? VertexShader { get; init; }
    public GpuShader? PixelShader { get; init; }
    public GpuShader? GeometryShader { get; init; }

    /// <summary>Empty for the fullscreen-triangle style passes most TiXL operators draw.</summary>
    public VertexAttribute[]? VertexLayout { get; init; }

    public Topology Topology { get; init; }
    public int PatchControlPoints { get; init; }
    public RasterState Rasterizer { get; init; }
    public DepthState DepthStencil { get; init; }
    public bool AlphaToCoverage { get; init; }
    public BlendTargetStates Blend { get; init; }
    public int RenderTargetCount { get; init; }

    /// <summary>
    /// Formats of the bound targets. Dynamic rendering builds the pipeline against these instead of against a
    /// render pass object, which is why the compatibility layer can only resolve a pipeline once targets are set.
    /// </summary>
    public FormatSet RenderTargetFormats { get; init; }

    public Format DepthStencilFormat { get; init; }
    public SampleDescription Samples { get; init; }
}

[System.Runtime.CompilerServices.InlineArray(BlendTargetStates.MaxRenderTargets)]
public struct FormatSet
{
    private Format _element0;
}

public readonly record struct ComputePipelineDescription
{
    public GpuShader? ComputeShader { get; init; }
}

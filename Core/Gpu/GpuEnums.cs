// GPU abstraction enums: on Windows these alias SharpDX types; on Linux they're natively defined.
// This allows operator code to use the same type names on all platforms via csproj Using aliases.
#if PLATFORM_WINDOWS

global using T3BindFlags = SharpDX.Direct3D11.BindFlags;
global using T3ResourceUsage = SharpDX.Direct3D11.ResourceUsage;
global using T3CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags;
global using T3ResourceOptionFlags = SharpDX.Direct3D11.ResourceOptionFlags;
global using T3Format = SharpDX.DXGI.Format;
global using T3PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology;
global using T3CullMode = SharpDX.Direct3D11.CullMode;
global using T3FillMode = SharpDX.Direct3D11.FillMode;
global using T3BlendOption = SharpDX.Direct3D11.BlendOption;
global using T3BlendOperation = SharpDX.Direct3D11.BlendOperation;
global using T3Comparison = SharpDX.Direct3D11.Comparison;
global using T3TextureAddressMode = SharpDX.Direct3D11.TextureAddressMode;
global using T3Filter = SharpDX.Direct3D11.Filter;
global using T3ColorWriteMaskFlags = SharpDX.Direct3D11.ColorWriteMaskFlags;
global using T3UnorderedAccessViewBufferFlags = SharpDX.Direct3D11.UnorderedAccessViewBufferFlags;

#else

using System;

namespace T3.Core.Gpu;

[Flags]
public enum BindFlags
{
    None = 0,
    VertexBuffer = 1,
    IndexBuffer = 2,
    ConstantBuffer = 4,
    ShaderResource = 8,
    StreamOutput = 16,
    RenderTarget = 32,
    DepthStencil = 64,
    UnorderedAccess = 128,
}

public enum ResourceUsage
{
    Default = 0,
    Immutable = 1,
    Dynamic = 2,
    Staging = 3,
}

[Flags]
public enum CpuAccessFlags
{
    None = 0,
    Write = 0x10000,
    Read = 0x20000,
}

[Flags]
public enum ResourceOptionFlags
{
    None = 0,
    GenerateMipMaps = 1,
    Shared = 2,
    TextureCube = 4,
    DrawIndirectArguments = 16,
    BufferStructured = 64,
}

public enum Format
{
    Unknown = 0,
    R32G32B32A32_Typeless = 1,
    R32G32B32A32_Float = 2,
    R32G32B32A32_UInt = 3,
    R32G32B32A32_SInt = 4,
    R16G16B16A16_Typeless = 9,
    R16G16B16A16_Float = 10,
    R16G16B16A16_UNorm = 11,
    R16G16B16A16_UInt = 12,
    R32G32_Float = 16,
    R32_Typeless = 39,
    D32_Float = 40,
    R32_Float = 41,
    R32_UInt = 42,
    R16G16_Float = 34,
    R8G8B8A8_Typeless = 27,
    R8G8B8A8_UNorm = 28,
    R8G8B8A8_UNorm_SRgb = 29,
    R8G8B8A8_SNorm = 31,
    R16_UInt = 57,
    R8_UNorm = 61,
    BC1_UNorm = 71,
    BC2_UNorm = 74,
    BC3_UNorm = 77,
    B8G8R8A8_UNorm = 87,
    B8G8R8A8_Typeless = 90,
    B8G8R8X8_UNorm = 88,
    NV12 = 103,
    P8 = 113,
    A8P8 = 114,
    AI44 = 111,
    IA44 = 112,
}

public enum PrimitiveTopology
{
    Undefined = 0,
    PointList = 1,
    LineList = 2,
    LineStrip = 3,
    TriangleList = 4,
    TriangleStrip = 5,
}

public enum CullMode
{
    None = 1,
    Front = 2,
    Back = 3,
}

public enum FillMode
{
    Wireframe = 2,
    Solid = 3,
}

public enum BlendOption
{
    Zero = 1,
    One = 2,
    SourceAlpha = 5,
    InverseSourceAlpha = 6,
}

public enum BlendOperation
{
    Add = 1,
    Subtract = 2,
    ReverseSubtract = 3,
    Minimum = 4,
    Maximum = 5,
}

public enum Comparison
{
    Never = 1,
    Less = 2,
    Equal = 3,
    LessEqual = 4,
    Greater = 5,
    NotEqual = 6,
    GreaterEqual = 7,
    Always = 8,
}

public enum TextureAddressMode
{
    Wrap = 1,
    Mirror = 2,
    Clamp = 3,
    Border = 4,
    MirrorOnce = 5,
}

public enum Filter
{
    MinMagMipPoint = 0,
    MinMagMipLinear = 21,
    Anisotropic = 85,
}

[Flags]
public enum ColorWriteMaskFlags : byte
{
    Red = 1,
    Green = 2,
    Blue = 4,
    Alpha = 8,
    All = Red | Green | Blue | Alpha,
}

[Flags]
public enum UnorderedAccessViewBufferFlags
{
    None = 0,
    Raw = 1,
    Append = 2,
    Counter = 4,
}

#endif

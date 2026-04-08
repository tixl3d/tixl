// GPU abstraction structs: on Windows these alias SharpDX types; on Linux they're natively defined.
#if PLATFORM_WINDOWS

global using T3Texture2DDescription = SharpDX.Direct3D11.Texture2DDescription;
global using T3Texture3DDescription = SharpDX.Direct3D11.Texture3DDescription;
global using T3RenderTargetBlendDescription = SharpDX.Direct3D11.RenderTargetBlendDescription;
global using T3SampleDescription = SharpDX.DXGI.SampleDescription;
global using T3RawRectangle = SharpDX.Mathematics.Interop.RawRectangle;
global using T3RawViewportF = SharpDX.Mathematics.Interop.RawViewportF;

#else

namespace T3.Core.Gpu;

public struct SampleDescription(int count, int quality)
{
    public int Count = count;
    public int Quality = quality;
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

public struct RenderTargetBlendDescription
{
    public bool IsBlendEnabled;
    public BlendOption SourceBlend;
    public BlendOption DestinationBlend;
    public BlendOperation BlendOperation;
    public BlendOption SourceAlphaBlend;
    public BlendOption DestinationAlphaBlend;
    public BlendOperation AlphaBlendOperation;
    public ColorWriteMaskFlags RenderTargetWriteMask;
}

public struct RawRectangle(int left, int top, int right, int bottom)
{
    public int Left = left;
    public int Top = top;
    public int Right = right;
    public int Bottom = bottom;
}

public struct RawViewportF
{
    public float X;
    public float Y;
    public float Width;
    public float Height;
    public float MinDepth;
    public float MaxDepth;
}

#endif

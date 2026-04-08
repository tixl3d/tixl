// GPU resource abstractions: on Windows these alias SharpDX types; on Linux they're stub types
// that will be backed by a Veldrid implementation in Phase 1.3.
//
// Operator code uses these types via csproj Using aliases (e.g., "Buffer" -> this type).
// The types need to be API-compatible with SharpDX usage patterns.
#if PLATFORM_WINDOWS

// On Windows, operator code continues to use SharpDX types directly via Using aliases.
// No abstraction layer is needed at the operator level - SharpDX is available.

#else

using System;

namespace T3.Core.Gpu;

/// <summary>
/// Cross-platform GPU buffer abstraction. On Linux, backed by Veldrid DeviceBuffer.
/// </summary>
public class Buffer : GpuResource
{
    public BufferDescription Description;

    public struct BufferDescription
    {
        public int SizeInBytes;
        public BindFlags BindFlags;
        public ResourceUsage Usage;
        public CpuAccessFlags CpuAccessFlags;
        public ResourceOptionFlags OptionFlags;
        public int StructureByteStride;
    }
}

/// <summary>
/// Cross-platform shader resource view abstraction.
/// </summary>
public class ShaderResourceView : GpuResourceView
{
    public ShaderResourceViewDescription Description;
    public IntPtr NativePointer => IntPtr.Zero;

    public ShaderResourceView() { }
    public ShaderResourceView(object device, object resource) { }

    public static explicit operator IntPtr(ShaderResourceView? srv) => srv?.NativePointer ?? IntPtr.Zero;

    public struct ShaderResourceViewDescription
    {
        public BufferDescription Buffer;

        public struct BufferDescription
        {
            public int ElementCount;
            public int FirstElement;
        }
    }
}

/// <summary>
/// Cross-platform unordered access view abstraction.
/// </summary>
public class UnorderedAccessView : GpuResourceView
{
}

/// <summary>
/// Cross-platform render target view abstraction.
/// </summary>
public class RenderTargetView : GpuResourceView
{
    public RenderTargetView() { }
    public RenderTargetView(object device, object resource) { }
}

/// <summary>
/// Cross-platform depth stencil view abstraction.
/// </summary>
public class DepthStencilView : GpuResourceView
{
}

/// <summary>
/// Cross-platform sampler state abstraction.
/// </summary>
public class SamplerState : GpuResource
{
}

/// <summary>
/// Cross-platform blend state abstraction.
/// </summary>
public class BlendState : GpuResource
{
}

/// <summary>
/// Cross-platform depth stencil state abstraction.
/// </summary>
public class DepthStencilState : GpuResource
{
}

/// <summary>
/// Cross-platform rasterizer state abstraction.
/// </summary>
public class RasterizerState : GpuResource
{
}

/// <summary>
/// Cross-platform input layout abstraction.
/// </summary>
public class InputLayout : GpuResource
{
}

/// <summary>
/// Base class for GPU resources that can be disposed and named.
/// </summary>
public abstract class GpuResource : IDisposable
{
    public string? DebugName { get; set; }
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        DisposeNative();
        GC.SuppressFinalize(this);
    }

    protected virtual void DisposeNative() { }

    ~GpuResource() => Dispose();
}

/// <summary>
/// Base class for GPU resource views (SRV, UAV, RTV, DSV).
/// </summary>
public abstract class GpuResourceView : GpuResource
{
}

#endif

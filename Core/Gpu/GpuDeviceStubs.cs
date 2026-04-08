// Stub types for Linux that mirror SharpDX types used by operator code.
// These allow operator code to compile on Linux even without a real GPU backend.
// Runtime calls to these stubs will be no-ops until the Veldrid backend is implemented.
#if !PLATFORM_WINDOWS

using System;
using System.Numerics;
using T3.Core.DataTypes;
using T3.Core.Gpu;

namespace T3.Core.Resource
{
    // Stub ResourceManager members that operators call directly
    public static partial class ResourceManager
    {
        public static GpuDeviceStub Device { get; set; } = new();

        public static void Init(GpuDeviceStub device) => Device = device;

        public static void SetupStructuredBuffer<T>(T[] data, ref Gpu.Buffer buffer) where T : struct { }
        public static void SetupStructuredBuffer<T>(ref Gpu.Buffer buffer, int count) where T : struct { }
        public static void CreateStructuredBufferSrv(Gpu.Buffer buffer, ref ShaderResourceView srv) { }
        public static void CreateStructuredBufferUav(Gpu.Buffer buffer, UnorderedAccessViewBufferFlags flags, ref UnorderedAccessView uav) { }
        public static void CreateBufferUav<T>(Gpu.Buffer buffer, UnorderedAccessViewBufferFlags flags, ref UnorderedAccessView uav) where T : struct { }
        public static void SetupConstBuffer<T>(T data, ref Gpu.Buffer buffer) where T : struct { }
        public static Resource<Texture2D> CreateTextureResource(string relativePath, object instance) => default;
    }

    public class GpuDeviceStub
    {
        public DeviceContextStub ImmediateContext { get; } = new();
    }

    public class DeviceContextStub
    {
        public RasterizerStageStub Rasterizer { get; } = new();
        public OutputMergerStageStub OutputMerger { get; } = new();
        public VertexShaderStageStub VertexShader { get; } = new();
        public PixelShaderStageStub PixelShader { get; } = new();
        public ComputeShaderStageStub ComputeShader { get; } = new();
        public GeometryShaderStageStub GeometryShader { get; } = new();
        public InputAssemblerStageStub InputAssembler { get; } = new();

        public void ClearState() { }
        public void Flush() { }
        public void Draw(int vertexCount, int startVertex) { }
        public void DrawIndexed(int indexCount, int startIndex, int baseVertex) { }
        public void Dispatch(int x, int y, int z) { }
        public void ClearRenderTargetView(RenderTargetView rtv, Color4 color) { }
        public void ClearDepthStencilView(DepthStencilView dsv, int flags, float depth, byte stencil) { }
        public void CopyResource(object src, object dst) { }
        public void MapSubresource(object resource, int subresource, int mapType, int flags, out DataStream stream) { stream = new DataStream(); }
        public void UnmapSubresource(object resource, int subresource) { }
    }

    public class RasterizerStageStub
    {
        public RasterizerState State { get; set; }
        public void SetViewport(Viewport viewport) { }
        public void SetViewport(ViewportF viewport) { }
        public void SetViewports(params Viewport[] viewports) { }
        public T[] GetViewports<T>() where T : struct => Array.Empty<T>();
        public void GetViewports(Viewport[] viewports) { }
        public void SetScissorRectangle(int left, int top, int right, int bottom) { }
    }

    public class OutputMergerStageStub
    {
        public void SetTargets(params object[] targets) { }
        public void SetBlendState(BlendState state) { }
        public DepthStencilState DepthStencilState { get; set; }
        public int DepthStencilReference { get; set; }
    }

    public class VertexShaderStageStub
    {
        public void Set(object shader) { }
        public void SetConstantBuffer(int slot, object buffer) { }
        public void SetShaderResource(int slot, object srv) { }
    }

    public class PixelShaderStageStub
    {
        public void Set(object shader) { }
        public void SetConstantBuffer(int slot, object buffer) { }
        public void SetShaderResource(int slot, object srv) { }
        public void SetSampler(int slot, object sampler) { }
    }

    public class ComputeShaderStageStub
    {
        public void Set(object shader) { }
        public object? Get() => null;
        public void SetConstantBuffer(int slot, object buffer) { }
        public object?[] GetConstantBuffers(int start, int count) => new object?[count];
        public void SetShaderResource(int slot, object srv) { }
        public object?[] GetShaderResources(int start, int count) => new object?[count];
        public void SetUnorderedAccessView(int slot, object uav) { }
        public void SetUnorderedAccessView(int slot, object uav, int initialCount) { }
        public object?[] GetUnorderedAccessViews(int start, int count) => new object?[count];
    }

    public class GeometryShaderStageStub
    {
        public void Set(object shader) { }
        public void SetConstantBuffer(int slot, object buffer) { }
        public void SetShaderResource(int slot, object srv) { }
    }

    public class InputAssemblerStageStub
    {
        public PrimitiveTopology PrimitiveTopology { get; set; }
        public InputLayout InputLayout { get; set; }
        public void SetVertexBuffers(int slot, object binding) { }
        public void SetIndexBuffer(object buffer, Format format, int offset) { }
    }
}

namespace T3.Core.Resource
{
    // Stubs for utility types that are in #if PLATFORM_WINDOWS wrapped files
    public class StructuredBufferReadAccess
    {
        public record struct ReadRequestItem(string Name);
    }

    public class TextureReadAccess
    {
        public record struct ReadRequestItem(string Name);
    }
}

namespace T3.Core.Gpu
{
    // SharpDX utility types used directly by operator code
    public struct Color4
    {
        public float Red, Green, Blue, Alpha;
        public Color4(float r, float g, float b, float a) { Red = r; Green = g; Blue = b; Alpha = a; }
        public Color4(Vector4 v) : this(v.X, v.Y, v.Z, v.W) { }
    }

    public struct RawColor4
    {
        public float R, G, B, A;
        public RawColor4(float r, float g, float b, float a) { R = r; G = g; B = b; A = a; }
    }

    public struct RawColorBGRA { public byte B, G, R, A; }

    public struct Size2
    {
        public int Width, Height;
        public Size2(int w, int h) { Width = w; Height = h; }
    }

    public struct RectangleF { public float X, Y, Width, Height; }

    // These mirror SharpDX.Direct3D11.Device/DeviceContext for operator code that uses `using Device = ...`
    public class Device : T3.Core.Resource.GpuDeviceStub { }
    public class DeviceContext : T3.Core.Resource.DeviceContextStub { }

    // Stubs for MediaFoundation/DirectShow types used by video operators
    public class MediaEngine : IDisposable { public void Dispose() {} }
    public enum MediaEngineEvent { }
    public enum MediaEngineErr { }
    public class DXGIDeviceManager : IDisposable { public void Dispose() {} }
    public struct VideoNormalizedRect { public float Left, Top, Right, Bottom; }

    // Stub for SharpDX.Direct3D11.Resource static properties
    public static class Resource
    {
        public const int MaximumTexture2DSize = 16384;
    }


    public class DataStream : IDisposable
    {
        public IntPtr DataPointer => IntPtr.Zero;
        public long Length => 0;
        public void Write<T>(T value) where T : struct { }
        public void Dispose() { }
    }

    public struct BufferDescription
    {
        public int SizeInBytes;
        public BindFlags BindFlags;
        public ResourceUsage Usage;
        public CpuAccessFlags CpuAccessFlags;
        public ResourceOptionFlags OptionFlags;
        public int StructureByteStride;
    }

    [Flags]
    public enum AsynchronousFlags { None = 0, DoNotFlush = 1 }

    public struct QueryDescription
    {
        public QueryType Type;
        public MiscFlags Flags;
        public enum QueryType { Event = 0, Occlusion = 1, Timestamp = 2, TimestampDisjoint = 3, PipelineStatistics = 4 }
        public enum MiscFlags { None = 0 }
    }

    public class Query : GpuResource
    {
        public Query(object device, QueryDescription desc) { }
    }

    public struct Viewport
    {
        public float X, Y, Width, Height, MinDepth, MaxDepth;
        public Viewport(float x, float y, float w, float h, float minD, float maxD)
        { X = x; Y = y; Width = w; Height = h; MinDepth = minD; MaxDepth = maxD; }
    }

    public struct ViewportF
    {
        public float X, Y, Width, Height, MinDepth, MaxDepth;
        public ViewportF(float x, float y, float w, float h, float minD = 0f, float maxD = 1f)
        { X = x; Y = y; Width = w; Height = h; MinDepth = minD; MaxDepth = maxD; }
    }
}

#endif

using SharpDX.Direct3D11;
using T3.Graphics;
using D3D = SharpDX.Direct3D;
using D3D11Device = SharpDX.Direct3D11.Device;
using DXGI = SharpDX.DXGI;

namespace T3.Graphics.D3D11;

/// <summary>
/// The backend API on top of D3D11. It exists so the port can move the whole codebase onto the new API while
/// Windows keeps rendering exactly as before; it is deleted once the Vulkan backend carries everything.
/// </summary>
public sealed class D3D11Backend : IGraphicsBackend, IDisposable
{
    public D3D11Backend(D3D11Device device, bool ownsDevice = false)
    {
        Device = device;
        _ownsDevice = ownsDevice;
        _commands = new D3D11CommandList(device);

        using var dxgiDevice = device.QueryInterfaceOrNull<DXGI.Device>();
        AdapterName = dxgiDevice?.Adapter?.Description.Description ?? "Direct3D 11";
    }

    public static D3D11Backend Create(bool debug = false)
    {
        var flags = DeviceCreationFlags.BgraSupport | (debug ? DeviceCreationFlags.Debug : DeviceCreationFlags.None);
        return new D3D11Backend(new D3D11Device(D3D.DriverType.Hardware, flags), ownsDevice: true);
    }

    /// <summary>The device itself stays reachable: video decoding and Spout hand its native pointer to other libraries.</summary>
    public D3D11Device Device { get; }

    public string AdapterName { get; }

    public IntPtr NativeDeviceHandle => Device.NativePointer;

    public void SetMultithreadProtected(bool enabled)
    {
        using var multithread = Device.QueryInterfaceOrNull<SharpDX.Direct3D11.Multithread>();

        if (multithread != null)
            multithread.SetMultithreadProtected(enabled);
    }

    public GpuTexture? AdoptTexture(IntPtr nativeHandle, in TextureDescription description, string? label = null)
    {
        if (nativeHandle == IntPtr.Zero)
            return null;

        // The caller keeps ownership of the texture; the wrapper must not release it.
        var texture = new Texture2D(nativeHandle);
        return new D3D11Texture(Device, texture, description, label) { OwnsResource = false };
    }

    public GpuTexture CreateTexture(in TextureDescription description, ReadOnlySpan<byte> initialData, string? label = null)
    {
        var resource = CreateResource(description, initialData);

        if (label != null)
            resource.DebugName = label;

        return new D3D11Texture(Device, resource, description, label);
    }

    public unsafe GpuTexture CreateTexture(in TextureDescription description, ReadOnlySpan<SubresourceData> initialData, string? label = null)
    {
        var format = Convert.ToDxgi(description.Format);
        var bindFlags = Convert.ToD3D(description.Usage);
        var usage = Convert.ToD3D(description.Memory);
        var cpuAccess = Convert.ToCpuAccess(description.Memory);

        var boxes = new SharpDX.DataBox[initialData.Length];

        for (var i = 0; i < initialData.Length; i++)
        {
            boxes[i] = new SharpDX.DataBox(initialData[i].Data, initialData[i].RowPitch, initialData[i].SlicePitch);
        }

        Resource resource;

        switch (description.Dimension)
        {
            case TextureDimension.Texture3D:
                resource = new Texture3D(Device,
                                         new Texture3DDescription
                                             {
                                                 Width = description.Width,
                                                 Height = description.Height,
                                                 Depth = description.Depth,
                                                 MipLevels = Math.Max(1, description.MipLevels),
                                                 Format = format,
                                                 Usage = usage,
                                                 BindFlags = bindFlags,
                                                 CpuAccessFlags = cpuAccess,
                                             },
                                         boxes);
                break;

            case TextureDimension.Texture1D:
                resource = new Texture1D(Device,
                                         new Texture1DDescription
                                             {
                                                 Width = description.Width,
                                                 ArraySize = Math.Max(1, description.ArraySize),
                                                 MipLevels = Math.Max(1, description.MipLevels),
                                                 Format = format,
                                                 Usage = usage,
                                                 BindFlags = bindFlags,
                                                 CpuAccessFlags = cpuAccess,
                                             },
                                         boxes);
                break;

            default:
                resource = new Texture2D(Device,
                                         new Texture2DDescription
                                             {
                                                 Width = description.Width,
                                                 Height = description.Height,
                                                 ArraySize = Math.Max(1, description.ArraySize),
                                                 MipLevels = Math.Max(1, description.MipLevels),
                                                 Format = format,
                                                 SampleDescription = new DXGI.SampleDescription(Math.Max(1, description.Samples.Count),
                                                                                                description.Samples.Quality),
                                                 Usage = usage,
                                                 BindFlags = bindFlags,
                                                 CpuAccessFlags = cpuAccess,
                                                 OptionFlags = description.Dimension == TextureDimension.TextureCube
                                                                   ? ResourceOptionFlags.TextureCube
                                                                   : ResourceOptionFlags.None,
                                             },
                                         boxes);
                break;
        }

        if (label != null)
            resource.DebugName = label;

        return new D3D11Texture(Device, resource, description, label);
    }

    private unsafe Resource CreateResource(in TextureDescription description, ReadOnlySpan<byte> initialData)
    {
        var format = Convert.ToDxgi(description.Format);
        var bindFlags = Convert.ToD3D(description.Usage);
        var usage = Convert.ToD3D(description.Memory);
        var cpuAccess = Convert.ToCpuAccess(description.Memory);
        var options = description.Dimension == TextureDimension.TextureCube ? ResourceOptionFlags.TextureCube : ResourceOptionFlags.None;

        fixed (byte* data = initialData)
        {
            switch (description.Dimension)
            {
                case TextureDimension.Texture1D:
                {
                    var d3d11Description = new Texture1DDescription
                                               {
                                                   Width = description.Width,
                                                   ArraySize = Math.Max(1, description.ArraySize),
                                                   MipLevels = Math.Max(1, description.MipLevels),
                                                   Format = format,
                                                   Usage = usage,
                                                   BindFlags = bindFlags,
                                                   CpuAccessFlags = cpuAccess,
                                               };

                    return initialData.IsEmpty
                               ? new Texture1D(Device, d3d11Description)
                               : new Texture1D(Device, d3d11Description, [new SharpDX.DataBox((IntPtr)data)]);
                }

                case TextureDimension.Texture3D:
                {
                    var d3d11Description = new Texture3DDescription
                                               {
                                                   Width = description.Width,
                                                   Height = description.Height,
                                                   Depth = description.Depth,
                                                   MipLevels = Math.Max(1, description.MipLevels),
                                                   Format = format,
                                                   Usage = usage,
                                                   BindFlags = bindFlags,
                                                   CpuAccessFlags = cpuAccess,
                                               };

                    return initialData.IsEmpty
                               ? new Texture3D(Device, d3d11Description)
                               : new Texture3D(Device, d3d11Description,
                                               [new SharpDX.DataBox((IntPtr)data, RowPitch(description), RowPitch(description) * description.Height)]);
                }

                default:
                {
                    var d3d11Description = new Texture2DDescription
                                               {
                                                   Width = description.Width,
                                                   Height = description.Height,
                                                   ArraySize = Math.Max(1, description.ArraySize),
                                                   MipLevels = Math.Max(1, description.MipLevels),
                                                   Format = format,
                                                   SampleDescription = new DXGI.SampleDescription(Math.Max(1, description.Samples.Count),
                                                                                                  description.Samples.Quality),
                                                   Usage = usage,
                                                   BindFlags = bindFlags,
                                                   CpuAccessFlags = cpuAccess,
                                                   OptionFlags = options,
                                               };

                    return initialData.IsEmpty
                               ? new Texture2D(Device, d3d11Description)
                               : new Texture2D(Device, d3d11Description, [new SharpDX.DataRectangle((IntPtr)data, RowPitch(description))]);
                }
            }
        }
    }

    /// <summary>
    /// Initial data is uploaded tightly packed, as every caller in TiXL does it; a format whose block size is
    /// not a whole number of bytes per pixel has to use a copy instead.
    /// </summary>
    private static int RowPitch(in TextureDescription description) => description.Width * FormatSizes.BytesPerPixel(description.Format);

    public GpuBuffer CreateBuffer(in GpuBufferDescription description, ReadOnlySpan<byte> initialData, string? label = null)
    {
        var options = description.StructureByteStride > 0 ? ResourceOptionFlags.BufferStructured : ResourceOptionFlags.None;

        if ((description.Usage & BufferUsage.Indirect) != 0)
            options |= ResourceOptionFlags.DrawIndirectArguments;

        var d3d11Description = new BufferDescription
                                   {
                                       SizeInBytes = description.SizeInBytes,
                                       Usage = Convert.ToD3D(description.Memory),
                                       BindFlags = Convert.ToD3D(description.Usage),
                                       CpuAccessFlags = Convert.ToCpuAccess(description.Memory),
                                       OptionFlags = options,
                                       StructureByteStride = description.StructureByteStride,
                                   };

        SharpDX.Direct3D11.Buffer buffer;
        unsafe
        {
            fixed (byte* data = initialData)
            {
                buffer = initialData.IsEmpty
                             ? new SharpDX.Direct3D11.Buffer(Device, d3d11Description)
                             : new SharpDX.Direct3D11.Buffer(Device, (IntPtr)data, d3d11Description);
            }
        }

        if (label != null)
            buffer.DebugName = label;

        return new D3D11Buffer(Device, buffer, description, label);
    }

    public GpuTextureView CreateTextureView(GpuTexture texture, in TextureViewDescription description, string? label = null)
        => new D3D11TextureView((D3D11Texture)texture, description, label);

    public GpuSampler CreateSampler(in SamplerDescription description, string? label = null)
    {
        var state = new SamplerState(Device, Convert.ToD3D(description));

        if (label != null)
            state.DebugName = label;

        return new D3D11Sampler(state, description, label);
    }

    /// <summary>The declared bindings are ignored: D3D11 resolves registers from the bytecode itself.</summary>
    public GpuShader CreateShader(ShaderStage stage, ReadOnlySpan<byte> code, string entryPoint, ReadOnlySpan<ShaderBinding> bindings = default,
                                  string? label = null)
    {
        // D3D11 takes DXBC; the entry point is already baked into it by the compiler.
        var bytecode = code.ToArray();

        DeviceChild shader = stage switch
                                 {
                                     ShaderStage.Vertex   => new VertexShader(Device, bytecode),
                                     ShaderStage.Pixel    => new PixelShader(Device, bytecode),
                                     ShaderStage.Geometry => new GeometryShader(Device, bytecode),
                                     _                    => new ComputeShader(Device, bytecode),
                                 };

        if (label != null)
            shader.DebugName = label;

        return new D3D11Shader(shader, stage, label) { Bytecode = stage == ShaderStage.Vertex ? bytecode : null };
    }

    public GpuSwapchain? CreateSwapchain(in SwapchainDescription description, in SurfaceTarget target, string? label = null)
    {
        if (target.Win32Window == nint.Zero)
        {
            GraphicsLog.Error?.Invoke("The window did not provide a Win32 handle to present into.");
            return null;
        }

        return new D3D11Swapchain(this, target.Win32Window, description, label);
    }

    public GpuPipeline GetOrCreatePipeline(in GraphicsPipelineDescription description)
    {
        lock (_pipelines)
        {
            if (_pipelines.TryGetValue(description, out var pipeline))
                return pipeline;

            pipeline = new D3D11Pipeline(Device, description, null);
            _pipelines[description] = pipeline;
            return pipeline;
        }
    }

    public GpuPipeline GetOrCreatePipeline(in ComputePipelineDescription description)
    {
        lock (_computePipelines)
        {
            if (_computePipelines.TryGetValue(description, out var pipeline))
                return pipeline;

            pipeline = new D3D11Pipeline(description, null);
            _computePipelines[description] = pipeline;
            return pipeline;
        }
    }

    /// <summary>Triangle fans are the one topology D3D11 dropped; everything else in the enum exists.</summary>
    public bool Supports(Topology topology) => topology != Topology.TriangleFan;

    public bool Supports(Format format, TextureUsage usage)
    {
        var support = Device.CheckFormatSupport(Convert.ToDxgi(format));

        if ((usage & TextureUsage.Sampled) != 0 && (support & FormatSupport.ShaderSample) == 0)
            return false;

        if ((usage & TextureUsage.RenderTarget) != 0 && (support & FormatSupport.RenderTarget) == 0)
            return false;

        if ((usage & TextureUsage.DepthStencil) != 0 && (support & FormatSupport.DepthStencil) == 0)
            return false;

        if ((usage & TextureUsage.Storage) != 0 && (support & FormatSupport.TypedUnorderedAccessView) == 0)
            return false;

        return true;
    }

    /// <summary>
    /// D3D11 has no frame boundary: there is nothing to reset and nothing to retire, because resources are
    /// released as soon as they are disposed and the driver tracks the rest.
    /// </summary>
    public ICommandList BeginFrame() => _commands;

    public void EndFrame(ICommandList commands)
    {
    }

    public unsafe MappedMemory MapForRead(GpuResource resource, int subresource)
    {
        var target = resource switch
                         {
                             D3D11Texture texture => texture.Resource,
                             D3D11Buffer buffer   => (Resource)buffer.Buffer,
                             _                    => null,
                         };

        if (target == null)
            return default;

        var box = Device.ImmediateContext.MapSubresource(target, subresource, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
        return new MappedMemory((void*)box.DataPointer, box.RowPitch, box.SlicePitch, box.SlicePitch);
    }

    public void Unmap(GpuResource resource, int subresource) => _commands.Unmap(resource, subresource);

    /// <summary>
    /// Completes synchronously. D3D11 gives no completion signal short of a query, and every caller in TiXL
    /// already owns a staging ring that waits the right number of frames before asking; the Vulkan backend is
    /// where this becomes genuinely asynchronous.
    /// </summary>
    public Task<ReadbackResult> ReadbackAsync(GpuTexture texture, int mipLevel, int arraySlice, CancellationToken cancellation = default)
    {
        if (texture is not D3D11Texture source)
            return Task.FromResult(new ReadbackResult(ReadOnlyMemory<byte>.Empty, 0, 0, null));

        var description = texture.Description with { Memory = MemoryKind.Readback, Usage = TextureUsage.CopyDestination };
        using var staging = (D3D11Texture)CreateTexture(description, ReadOnlySpan<byte>.Empty, "readback");

        Device.ImmediateContext.CopyResource(source.Resource, staging.Resource);
        var box = Device.ImmediateContext.MapSubresource(staging.Resource, mipLevel, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

        var bytes = new byte[box.SlicePitch];
        System.Runtime.InteropServices.Marshal.Copy(box.DataPointer, bytes, 0, bytes.Length);
        Device.ImmediateContext.UnmapSubresource(staging.Resource, mipLevel);

        return Task.FromResult(new ReadbackResult(bytes, box.RowPitch, box.SlicePitch, null));
    }

    public Task<ReadbackResult> ReadbackAsync(GpuBuffer buffer, CancellationToken cancellation = default)
    {
        if (buffer is not D3D11Buffer source)
            return Task.FromResult(new ReadbackResult(ReadOnlyMemory<byte>.Empty, 0, 0, null));

        var description = buffer.Description with { Memory = MemoryKind.Readback, Usage = BufferUsage.CopyDestination };
        using var staging = (D3D11Buffer)CreateBuffer(description, ReadOnlySpan<byte>.Empty, "readback");

        Device.ImmediateContext.CopyResource(source.Buffer, staging.Buffer);
        var box = Device.ImmediateContext.MapSubresource(staging.Buffer, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

        var bytes = new byte[buffer.Description.SizeInBytes];
        System.Runtime.InteropServices.Marshal.Copy(box.DataPointer, bytes, 0, bytes.Length);
        Device.ImmediateContext.UnmapSubresource(staging.Buffer, 0);

        return Task.FromResult(new ReadbackResult(bytes, bytes.Length, bytes.Length, null));
    }

    /// <summary>
    /// Empty on purpose: D3D11 pages resources in and out by itself, which is why TiXL never had to think
    /// about GPU memory. The Vulkan backend reports real numbers here and raises <see cref="MemoryPressure"/>.
    /// </summary>
    public MemoryReport QueryMemory() => new(0, 0, 0);

    public event Action<MemoryReport>? MemoryPressure;

    public void Dispose()
    {
        _commands.ReleaseInlineConstants();

        foreach (var pipeline in _pipelines.Values)
        {
            pipeline.Dispose();
        }

        foreach (var pipeline in _computePipelines.Values)
        {
            pipeline.Dispose();
        }

        MemoryPressure = null;

        if (_ownsDevice)
            Device.Dispose();
    }

    private readonly D3D11CommandList _commands;
    private readonly bool _ownsDevice;
    private readonly Dictionary<GraphicsPipelineDescription, D3D11Pipeline> _pipelines = [];
    private readonly Dictionary<ComputePipelineDescription, D3D11Pipeline> _computePipelines = [];
}

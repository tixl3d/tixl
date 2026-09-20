namespace T3.Graphics;

/// <summary>
/// What a backend has to provide. Shaped after Vulkan, not D3D11: resources are created with explicit usage,
/// state is baked into pipelines, work is recorded into a command list and a frame has a beginning and an end.
/// The compatibility layer in T3.Graphics.Compat translates D3D11-style calls into this; new code calls it
/// directly.
/// </summary>
public interface IGraphicsBackend
{
    string AdapterName { get; }

    /// <summary>
    /// Resource creation is thread-safe. TiXL loads images and builds buffers on worker threads while the
    /// main thread renders, and that has to keep working.
    /// </summary>
    GpuTexture CreateTexture(in TextureDescription description, ReadOnlySpan<byte> initialData, string? label = null);

    GpuBuffer CreateBuffer(in GpuBufferDescription description, ReadOnlySpan<byte> initialData, string? label = null);
    GpuTextureView CreateTextureView(GpuTexture texture, in TextureViewDescription description, string? label = null);
    GpuSampler CreateSampler(in SamplerDescription description, string? label = null);
    GpuShader CreateShader(ShaderStage stage, ReadOnlySpan<byte> code, string entryPoint, string? label = null);

    /// <summary>Cached by description; calling this per draw is the expected usage.</summary>
    GpuPipeline GetOrCreatePipeline(in GraphicsPipelineDescription description);

    GpuPipeline GetOrCreatePipeline(in ComputePipelineDescription description);

    /// <summary>True if the device can actually do this; topology and format support differ between drivers.</summary>
    bool Supports(Topology topology);

    bool Supports(Format format, TextureUsage usage);

    /// <summary>
    /// Starts a frame: retires the resources whose last user has finished and resets the upload ring.
    /// D3D11 has no equivalent, so the compatibility layer calls it from the render loop.
    /// </summary>
    ICommandList BeginFrame();

    /// <summary>Submits the frame's work and presents if a swapchain is attached.</summary>
    void EndFrame(ICommandList commands);

    /// <summary>
    /// Blocks until the GPU has finished with the resource and maps it for reading. The operators that copy,
    /// flush and map in the same frame (colour picking, point readback, video export, the visual tests) need
    /// exactly this, stall included; everything else should use <see cref="ReadbackAsync"/>.
    /// </summary>
    MappedMemory MapForRead(GpuResource resource, int subresource);

    void Unmap(GpuResource resource, int subresource);

    /// <summary>
    /// Copies a resource into CPU-visible memory and completes once the GPU is done with it. Screenshots, the
    /// visual test suite and readback operators use this instead of stalling the device.
    /// </summary>
    Task<ReadbackResult> ReadbackAsync(GpuTexture texture, int mipLevel, int arraySlice, CancellationToken cancellation = default);

    Task<ReadbackResult> ReadbackAsync(GpuBuffer buffer, CancellationToken cancellation = default);

    /// <summary>
    /// How much of the GPU's budget is in use. D3D11 pages resources out by itself; Vulkan does not, so
    /// callers that can free caches subscribe to <see cref="MemoryPressure"/> and act on it.
    /// </summary>
    MemoryReport QueryMemory();

    /// <summary>Raised when allocation is close to the budget, before an allocation actually fails.</summary>
    event Action<MemoryReport>? MemoryPressure;
}

/// <summary>
/// Recorded work. One per frame on the main thread to start with — the interface allows more, but nothing in
/// TiXL records in parallel today, and the compatibility layer assumes a single list because D3D11's immediate
/// context is single-threaded anyway.
/// </summary>
public interface ICommandList
{
    /// <summary>
    /// Begins rendering into the given views (dynamic rendering: no framebuffer object, no render pass).
    /// The pipeline used inside has to match their formats.
    /// </summary>
    void BeginRendering(ReadOnlySpan<GpuTextureView> colorTargets, GpuTextureView? depthTarget);

    void EndRendering();

    void Clear(GpuTextureView target, System.Numerics.Vector4 color);
    void ClearDepthStencil(GpuTextureView target, float depth, byte stencil, bool clearDepth, bool clearStencil);

    void SetViewport(in Viewport viewport);
    void SetScissor(in ScissorRect rect);
    void SetBlendConstants(System.Numerics.Vector4 factor, uint sampleMask);
    void SetStencilReference(int reference);

    void SetPipeline(GpuPipeline pipeline);

    /// <summary>
    /// Binds everything one shader stage reads or writes. <paramref name="set"/> is the descriptor set, and
    /// each <see cref="Binding.Slot"/> is the binding index the shader was compiled with — the register shifts
    /// of the Slang setup (s→0, b→16, t→32, u→160) land here.
    /// </summary>
    /// <remarks>
    /// One set per shader stage, numbered by <see cref="ShaderStage"/>. D3D11 gives every stage its own slot
    /// space, so a vertex shader's t0 and a pixel shader's t0 are different resources; a single set would
    /// collide. New code that does not carry D3D11's stage split can put everything in set 0.
    /// </remarks>
    void SetBindings(int set, ReadOnlySpan<Binding> bindings);

    /// <summary>
    /// Writes into the frame's upload ring and binds the result. Replaces D3D11's map/discard on a dynamic
    /// buffer, which is how nearly every TiXL operator updates its constants.
    /// </summary>
    void SetInlineConstants(int set, int slot, ReadOnlySpan<byte> data);

    /// <summary>
    /// Hands out memory for a whole-resource overwrite that never waits on the GPU. This is D3D11's
    /// Map(WriteDiscard): the backend takes a fresh region from the frame's upload ring and points the
    /// resource at it, so the write cannot collide with a draw that is still reading the old contents.
    /// </summary>
    MappedMemory MapForDiscard(GpuResource resource, int subresource);

    void Unmap(GpuResource resource, int subresource);

    /// <summary>Uploads into a region of a resource, as UpdateSubresource does.</summary>
    void UpdateResource(GpuResource resource, int subresource, ReadOnlySpan<byte> data, int rowPitch, int slicePitch);

    void SetVertexBuffers(int startSlot, ReadOnlySpan<VertexBufferView> buffers);
    void SetIndexBuffer(GpuBuffer? buffer, Format format, int offset);

    void Draw(int vertexCount, int instanceCount, int firstVertex, int firstInstance);
    void DrawIndexed(int indexCount, int instanceCount, int firstIndex, int vertexOffset, int firstInstance);
    void DrawIndirect(GpuBuffer arguments, int offset);
    void Dispatch(int groupsX, int groupsY, int groupsZ);
    void DispatchIndirect(GpuBuffer arguments, int offset);

    void CopyTexture(GpuTexture source, GpuTexture destination);
    void CopyBuffer(GpuBuffer source, int sourceOffset, GpuBuffer destination, int destinationOffset, int size);
    void ResolveTexture(GpuTexture source, GpuTexture destination, Format format);
    void GenerateMips(GpuTextureView view);

    /// <summary>
    /// Groups draws in RenderDoc and in GPU profiles. TiXL already names its passes for the profiler, so the
    /// names exist; they just have nowhere to go under D3D11 without an annotation interface.
    /// </summary>
    void PushDebugGroup(string name);

    void PopDebugGroup();
}

public enum BindingKind
{
    ConstantBuffer,
    SampledTexture,
    StorageTexture,
    StructuredBuffer,
    StorageBuffer,
    Sampler,
}

/// <summary>One shader binding. A struct so a draw can pass a stack-allocated span and allocate nothing.</summary>
public readonly record struct Binding
{
    public BindingKind Kind { get; init; }

    /// <summary>The index the shader was compiled with, after the register shifts.</summary>
    public int Slot { get; init; }

    public GpuTextureView? TextureView { get; init; }
    public GpuBuffer? Buffer { get; init; }
    public GpuSampler? Sampler { get; init; }

    /// <summary>Byte range inside <see cref="Buffer"/>. A zero size means the rest of the buffer.</summary>
    public int BufferOffset { get; init; }

    public int BufferSize { get; init; }

    /// <summary>For append/consume and counter buffers; -1 leaves the counter alone, as D3D11's -1 does.</summary>
    public int InitialCount { get; init; }
}

public readonly record struct VertexBufferView(GpuBuffer? Buffer, int Stride, int Offset);

public readonly record struct Viewport(float X, float Y, float Width, float Height, float MinDepth, float MaxDepth);

public readonly record struct ScissorRect(int X, int Y, int Width, int Height);

/// <summary>CPU-visible copy of a resource, valid until disposed.</summary>
public readonly struct ReadbackResult(ReadOnlyMemory<byte> data, int rowPitch, int slicePitch, IDisposable? owner) : IDisposable
{
    public readonly ReadOnlyMemory<byte> Data = data;
    public readonly int RowPitch = rowPitch;
    public readonly int SlicePitch = slicePitch;
    private readonly IDisposable? _owner = owner;

    public void Dispose() => _owner?.Dispose();
}

/// <summary>CPU-visible memory of a mapped resource, valid until the matching Unmap.</summary>
public readonly unsafe struct MappedMemory(void* data, int rowPitch, int slicePitch, int sizeInBytes)
{
    public readonly void* Data = data;
    public readonly int RowPitch = rowPitch;
    public readonly int SlicePitch = slicePitch;
    public readonly int SizeInBytes = sizeInBytes;

    public Span<byte> AsSpan() => new(Data, SizeInBytes);
}

public readonly record struct MemoryReport(long BudgetBytes, long UsedBytes, long ResourceBytes)
{
    public float Pressure => BudgetBytes <= 0 ? 0f : (float)((double)UsedBytes / BudgetBytes);
}

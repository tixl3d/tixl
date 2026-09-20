using T3.Graphics;

namespace Core.Tests;

/// <summary>
/// A backend that records what it is told instead of talking to a GPU. It exists so the compatibility layer's
/// translation — D3D11 state in, pipelines and bindings out — can be tested without a device, on any machine.
/// </summary>
internal sealed class FakeGraphicsBackend : IGraphicsBackend
{
    public string AdapterName => "fake";

    public readonly FakeCommandList Commands = new();

    public GpuTexture CreateTexture(in TextureDescription description, ReadOnlySpan<byte> initialData, string? label = null)
    {
        Textures.Add(description);
        return new FakeTexture(description, label);
    }

    public GpuBuffer CreateBuffer(in GpuBufferDescription description, ReadOnlySpan<byte> initialData, string? label = null)
    {
        Buffers.Add(description);
        return new FakeBuffer(description, label);
    }

    public GpuTextureView CreateTextureView(GpuTexture texture, in TextureViewDescription description, string? label = null)
        => new FakeTextureView(texture, description, label);

    public GpuSampler CreateSampler(in SamplerDescription description, string? label = null)
    {
        Samplers.Add(description);
        return new FakeSampler(description, label);
    }

    public GpuShader CreateShader(ShaderStage stage, ReadOnlySpan<byte> code, string entryPoint, string? label = null)
        => new FakeShader(stage, label);

    public GpuPipeline GetOrCreatePipeline(in GraphicsPipelineDescription description)
    {
        GraphicsPipelines.Add(description);
        return new FakePipeline(null);
    }

    public GpuPipeline GetOrCreatePipeline(in ComputePipelineDescription description)
    {
        ComputePipelines.Add(description);
        return new FakePipeline(null);
    }

    public bool Supports(Topology topology) => true;
    public bool Supports(Format format, TextureUsage usage) => true;

    public ICommandList BeginFrame()
    {
        Commands.Reset();
        return Commands;
    }

    public void EndFrame(ICommandList commands) => FramesEnded++;

    public MappedMemory MapForRead(GpuResource resource, int subresource) => default;
    public void Unmap(GpuResource resource, int subresource) { }

    public Task<ReadbackResult> ReadbackAsync(GpuTexture texture, int mipLevel, int arraySlice, CancellationToken cancellation = default)
        => Task.FromResult(new ReadbackResult(ReadOnlyMemory<byte>.Empty, 0, 0, null));

    public Task<ReadbackResult> ReadbackAsync(GpuBuffer buffer, CancellationToken cancellation = default)
        => Task.FromResult(new ReadbackResult(ReadOnlyMemory<byte>.Empty, 0, 0, null));

    public MemoryReport QueryMemory() => new(0, 0, 0);

    public event Action<MemoryReport>? MemoryPressure;

    public void RaiseMemoryPressure(MemoryReport report) => MemoryPressure?.Invoke(report);

    public readonly List<TextureDescription> Textures = [];
    public readonly List<GpuBufferDescription> Buffers = [];
    public readonly List<SamplerDescription> Samplers = [];
    public readonly List<GraphicsPipelineDescription> GraphicsPipelines = [];
    public readonly List<ComputePipelineDescription> ComputePipelines = [];
    public int FramesEnded;

    private sealed class FakeTexture(TextureDescription description, string? label) : GpuTexture(description, label)
    {
        protected override void ReleaseWhenRetired() { }
    }

    private sealed class FakeTextureView(GpuTexture texture, TextureViewDescription description, string? label)
        : GpuTextureView(texture, description, label)
    {
        public override ulong ImGuiTextureId => 1;
        protected override void ReleaseWhenRetired() { }
    }

    private sealed class FakeBuffer(GpuBufferDescription description, string? label) : GpuBuffer(description, label)
    {
        protected override void ReleaseWhenRetired() { }
    }

    private sealed class FakeSampler(SamplerDescription description, string? label) : GpuSampler(description, label)
    {
        protected override void ReleaseWhenRetired() { }
    }

    private sealed class FakeShader(ShaderStage stage, string? label) : GpuShader(stage, label)
    {
        protected override void ReleaseWhenRetired() { }
    }

    private sealed class FakePipeline(string? label) : GpuPipeline(label)
    {
        protected override void ReleaseWhenRetired() { }
    }
}

internal sealed class FakeCommandList : ICommandList
{
    public void Reset()
    {
        BindingsPerSet.Clear();
        ColorTargetCounts.Clear();
        RenderingBegun = 0;
        Draws = 0;
        Dispatches = 0;
    }

    public void BeginRendering(ReadOnlySpan<GpuTextureView> colorTargets, GpuTextureView? depthTarget)
    {
        RenderingBegun++;
        ColorTargetCounts.Add(colorTargets.Length);
    }

    public void EndRendering() { }

    public void Clear(GpuTextureView target, System.Numerics.Vector4 color) => Clears++;
    public void ClearDepthStencil(GpuTextureView target, float depth, byte stencil, bool clearDepth, bool clearStencil) { }

    public void SetViewport(in Viewport viewport) => Viewport = viewport;
    public void SetScissor(in ScissorRect rect) => Scissor = rect;
    public void SetBlendConstants(System.Numerics.Vector4 factor, uint sampleMask) { }
    public void SetStencilReference(int reference) { }
    public void SetPipeline(GpuPipeline pipeline) { }

    public void SetBindings(int set, ReadOnlySpan<Binding> bindings) => BindingsPerSet[set] = bindings.ToArray();

    public void SetInlineConstants(int set, int slot, ReadOnlySpan<byte> data) { }
    public MappedMemory MapForDiscard(GpuResource resource, int subresource) => default;
    public void Unmap(GpuResource resource, int subresource) { }
    public void UpdateResource(GpuResource resource, int subresource, ReadOnlySpan<byte> data, int rowPitch, int slicePitch) => Updates++;

    public void SetVertexBuffers(int startSlot, ReadOnlySpan<VertexBufferView> buffers) { }
    public void SetIndexBuffer(GpuBuffer? buffer, Format format, int offset) { }

    public void Draw(int vertexCount, int instanceCount, int firstVertex, int firstInstance) => Draws++;
    public void DrawIndexed(int indexCount, int instanceCount, int firstIndex, int vertexOffset, int firstInstance) => Draws++;
    public void DrawIndirect(GpuBuffer arguments, int offset) => Draws++;
    public void Dispatch(int groupsX, int groupsY, int groupsZ) => Dispatches++;
    public void DispatchIndirect(GpuBuffer arguments, int offset) => Dispatches++;

    public void CopyTexture(GpuTexture source, GpuTexture destination) => Copies++;
    public void CopyBuffer(GpuBuffer source, int sourceOffset, GpuBuffer destination, int destinationOffset, int size) => Copies++;
    public void ResolveTexture(GpuTexture source, GpuTexture destination, Format format) { }
    public void GenerateMips(GpuTextureView view) { }
    public void PushDebugGroup(string name) { }
    public void PopDebugGroup() { }

    public readonly Dictionary<int, Binding[]> BindingsPerSet = [];
    public readonly List<int> ColorTargetCounts = [];
    public Viewport Viewport;
    public ScissorRect Scissor;
    public int RenderingBegun;
    public int Draws;
    public int Dispatches;
    public int Clears;
    public int Copies;
    public int Updates;
}

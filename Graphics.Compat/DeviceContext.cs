using System.Numerics;
using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// D3D11's immediate context: a mutable state machine that operators set field by field and then draw from.
/// This class is where that model ends — a draw resolves the current state into a pipeline and a set of
/// bindings and records them into the backend's command list.
/// </summary>
/// <remarks>
/// Main thread only, like D3D11's immediate context. Resource creation on <see cref="Device"/> is not.
/// </remarks>
public sealed class DeviceContext
{
    internal DeviceContext(IGraphicsBackend backend)
    {
        Backend = backend;
        InputAssembler = new InputAssemblerStage();
        Rasterizer = new RasterizerStage();
        OutputMerger = new OutputMergerStage(this);
        VertexShader = new VertexShaderStage();
        PixelShader = new PixelShaderStage();
        GeometryShader = new GeometryShaderStage();
        ComputeShader = new ComputeShaderStage();
    }

    internal readonly IGraphicsBackend Backend;

    public InputAssemblerStage InputAssembler { get; }
    public RasterizerStage Rasterizer { get; }
    public OutputMergerStage OutputMerger { get; }
    public VertexShaderStage VertexShader { get; }
    public PixelShaderStage PixelShader { get; }
    public GeometryShaderStage GeometryShader { get; }
    public ComputeShaderStage ComputeShader { get; }

    /// <summary>
    /// Tessellation stages. TiXL has no hull or domain shaders, and the backends do not offer them; these
    /// exist so that code which only ever clears them keeps working.
    /// </summary>
    public UnusedShaderStage HullShader { get; } = new();

    public UnusedShaderStage DomainShader { get; } = new();

    #region frame
    internal void BeginFrame()
    {
        // Loading uploads textures and generates their mips before the render loop starts, which opens a
        // frame implicitly. Adopt that one rather than starting a second.
        _commands ??= Backend.BeginFrame();
        _renderingActive = false;
    }

    internal void EndFrame()
    {
        if (_commands == null)
            return;

        EndRendering();
        Backend.EndFrame(_commands);
        _commands = null;
    }

    /// <summary>
    /// D3D11's immediate context is usable whenever the device is, and TiXL relies on that while loading. The
    /// backends record into a frame, so open one on demand; the render loop's <see cref="EndFrame"/> submits it.
    /// </summary>
    private ICommandList Commands => _commands ??= Backend.BeginFrame();
    #endregion

    #region state save and restore
    /// <summary>
    /// Saves the named state so an enclosing operator can restore it later. Replaces reading state back from
    /// D3D11, which Vulkan cannot do — and which leaked an AddRef for every object it returned.
    /// </summary>
    /// <remarks>
    /// The push and the matching pop usually live in different operators: one sets state in its update, the
    /// enclosing operator pops it through <c>Command.RestoreAction</c>.
    /// </remarks>
    public void PushState(StateGroups groups)
    {
        var snapshot = _snapshotPool.Count > 0 ? _snapshotPool.Pop() : new StateSnapshot();
        snapshot.Capture(this, groups);
        _stateStack.Push(snapshot);
    }

    public void PopState()
    {
        if (_stateStack.Count == 0)
        {
            GraphicsLog.WarnOnce("PopState without a matching PushState; the graph restored more state than it saved.");
            return;
        }

        var snapshot = _stateStack.Pop();
        snapshot.Restore(this);
        _snapshotPool.Push(snapshot);
    }

    /// <summary>How deep the state stack is. A frame that ends with anything on it has an unbalanced operator.</summary>
    public int StateStackDepth => _stateStack.Count;

    private readonly Stack<StateSnapshot> _stateStack = new();
    private readonly Stack<StateSnapshot> _snapshotPool = new();
    #endregion

    #region draw and dispatch
    public void Draw(int vertexCount, int startVertex)
    {
        PrepareDraw();
        Commands.Draw(vertexCount, 1, startVertex, 0);
    }

    public void DrawInstanced(int vertexCountPerInstance, int instanceCount, int startVertex, int startInstance)
    {
        PrepareDraw();
        Commands.Draw(vertexCountPerInstance, instanceCount, startVertex, startInstance);
    }

    public void DrawIndexed(int indexCount, int startIndex, int baseVertex)
    {
        PrepareDraw();
        Commands.DrawIndexed(indexCount, 1, startIndex, baseVertex, 0);
    }

    public void DrawIndexedInstanced(int indexCountPerInstance, int instanceCount, int startIndex, int baseVertex, int startInstance)
    {
        PrepareDraw();
        Commands.DrawIndexed(indexCountPerInstance, instanceCount, startIndex, baseVertex, startInstance);
    }

    public void DrawInstancedIndirect(Buffer arguments, int alignedByteOffset)
    {
        if (arguments.GpuBuffer == null)
            return;

        PrepareDraw();
        Commands.DrawIndirect(arguments.GpuBuffer, alignedByteOffset);
    }

    public void Dispatch(int countX, int countY, int countZ)
    {
        if (!PrepareDispatch())
            return;

        Commands.Dispatch(countX, countY, countZ);
    }

    public void DispatchIndirect(Buffer arguments, int alignedByteOffset)
    {
        if (arguments.GpuBuffer == null || !PrepareDispatch())
            return;

        Commands.DispatchIndirect(arguments.GpuBuffer, alignedByteOffset);
    }
    #endregion

    #region clear, copy and upload
    public void ClearRenderTargetView(RenderTargetView view, Vector4 color)
    {
        if (view.GpuView == null)
            return;

        EndRendering();
        Commands.Clear(view.GpuView, color);
    }

    public void ClearDepthStencilView(DepthStencilView view, DepthStencilClearFlags flags, float depth, byte stencil)
    {
        if (view.GpuView == null)
            return;

        EndRendering();
        Commands.ClearDepthStencil(view.GpuView, depth, stencil,
                                   (flags & DepthStencilClearFlags.Depth) != 0,
                                   (flags & DepthStencilClearFlags.Stencil) != 0);
    }

    /// <summary>Unbinds everything, as D3D11 does. The next draw rebuilds the pipeline from scratch.</summary>
    public void ClearState()
    {
        EndRendering();
        InputAssembler.Clear();
        Rasterizer.Clear();
        OutputMerger.Clear();
        VertexShader.Clear();
        PixelShader.Clear();
        GeometryShader.Clear();
        ComputeShader.Clear();
    }

    public void CopyResource(Resource source, Resource destination)
    {
        EndRendering();

        switch (source, destination)
        {
            case (Texture { GpuTexture: { } from }, Texture { GpuTexture: { } to }):
                Commands.CopyTexture(from, to);
                break;

            case (Buffer { GpuBuffer: { } from }, Buffer { GpuBuffer: { } to }):
                Commands.CopyBuffer(from, 0, to, 0, Math.Min(from.Description.SizeInBytes, to.Description.SizeInBytes));
                break;
        }
    }

    /// <summary>Copies a box from one resource into another at an offset, as texture-array operators do.</summary>
    public void CopySubresourceRegion(Resource source, int sourceSubresource, ResourceRegion? region, Resource destination,
                                      int destinationSubresource, int x = 0, int y = 0, int z = 0)
    {
        EndRendering();

        if (source is Texture { GpuTexture: { } from } && destination is Texture { GpuTexture: { } to })
        {
            Commands.CopyTextureRegion(from, sourceSubresource, to, destinationSubresource, x, y, z);
            return;
        }

        if (source is Buffer { GpuBuffer: { } fromBuffer } && destination is Buffer { GpuBuffer: { } toBuffer })
        {
            var offset = region?.Left ?? 0;
            var size = region.HasValue ? region.Value.Right - region.Value.Left : fromBuffer.Description.SizeInBytes;
            Commands.CopyBuffer(fromBuffer, offset, toBuffer, x, size);
        }
    }

    /// <summary>Copies an append or consume buffer's counter into a constant buffer, for indirect draws.</summary>
    public void CopyStructureCount(Buffer destination, int destinationOffset, UnorderedAccessView source)
    {
        GraphicsLog.WarnOnce("CopyStructureCount is not implemented by the backends yet; the count is left unchanged.");
    }

    public void ResolveSubresource(Resource source, int sourceSubresource, Resource destination, int destinationSubresource, Format format)
    {
        if (source is not Texture { GpuTexture: { } from } || destination is not Texture { GpuTexture: { } to })
            return;

        EndRendering();
        Commands.ResolveTexture(from, to, format);
    }

    public void GenerateMips(ShaderResourceView view)
    {
        if (view.GpuView == null)
            return;

        EndRendering();
        Commands.GenerateMips(view.GpuView);
    }

    public unsafe void UpdateSubresource(Resource resource, int subresource, ReadOnlySpan<byte> data, int rowPitch, int slicePitch)
    {
        if (resource.Native == null)
            return;

        Commands.UpdateResource(resource.Native, subresource, data, rowPitch, slicePitch);
    }

    public void UpdateSubresource<T>(ref T value, Resource resource) where T : unmanaged
    {
        UpdateSubresource(resource, 0, System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)), 0, 0);
    }

    public void UpdateSubresource<T>(T[] values, Resource resource) where T : unmanaged
    {
        UpdateSubresource(resource, 0, System.Runtime.InteropServices.MemoryMarshal.AsBytes<T>(values), 0, 0);
    }

    public unsafe void UpdateSubresource(DataBox source, Resource resource, int subresource = 0)
    {
        if (resource.Native == null || source.DataPointer == IntPtr.Zero)
            return;

        var size = SubresourceByteSize(source, resource, subresource);
        if (size <= 0)
            return;

        Commands.UpdateResource(resource.Native, subresource, new ReadOnlySpan<byte>((void*)source.DataPointer, size),
                                source.RowPitch, source.SlicePitch);
    }

    /// <summary>
    /// How many bytes a <see cref="DataBox"/> covers. D3D11 ignores the pitches when the target is a buffer,
    /// and callers routinely leave the slice pitch at zero for a 2D texture. Taking the pitches at face value
    /// uploads a single row — or nothing at all — and the copy then reads past the staging buffer.
    /// </summary>
    private static int SubresourceByteSize(in DataBox source, Resource resource, int subresource)
    {
        if (resource is Buffer buffer)
            return buffer.Description.SizeInBytes;

        if (source.SlicePitch > 0)
            return source.SlicePitch;

        if (resource is Texture2D texture2d && source.RowPitch > 0)
        {
            var mip = subresource % MipLevelsOf(resource);
            return source.RowPitch * Math.Max(1, texture2d.Description.Height >> mip);
        }

        return Math.Max(source.SlicePitch, source.RowPitch);
    }

    /// <summary>
    /// The region form. The backend uploads whole subresources, so a partial update writes the rows it was
    /// given at the region's pitch — which is what every caller in TiXL passes anyway.
    /// </summary>
    public void UpdateSubresource(DataBox source, Resource resource, int subresource, ResourceRegion region)
        => UpdateSubresource(source, resource, subresource);

    /// <summary>The pointer-and-pitch form, which is how the DDS loader walks a file's mips.</summary>
    public unsafe void UpdateSubresource(Resource resource, int subresource, ResourceRegion? region, IntPtr data, int rowPitch, int slicePitch)
    {
        if (resource.Native == null || data == IntPtr.Zero)
            return;

        var size = Math.Max(slicePitch, rowPitch);
        Commands.UpdateResource(resource.Native, subresource, new ReadOnlySpan<byte>((void*)data, size), rowPitch, slicePitch);
    }

    /// <summary>
    /// Maps a resource. <see cref="MapMode.WriteDiscard"/> takes fresh memory from the frame's upload ring and
    /// never waits; <see cref="MapMode.Read"/> blocks until the GPU is done, exactly as D3D11 does.
    /// </summary>
    public DataBox MapSubresource(Resource resource, int subresource, MapMode mode, MapFlags flags)
    {
        if (resource.Native == null)
            return default;

        var mapped = mode switch
                         {
                             MapMode.Read or MapMode.ReadWrite => Backend.MapForRead(resource.Native, subresource),
                             _                                 => Commands.MapForDiscard(resource.Native, subresource),
                         };

        return new DataBox(mapped);
    }

    public DataBox MapSubresource(Resource resource, int subresource, MapMode mode, MapFlags flags, out DataStream stream)
    {
        var box = MapSubresource(resource, subresource, mode, flags);
        var size = SizeOf(resource);
        stream = new DataStream(box.DataPointer, size, mode != MapMode.WriteDiscard, mode != MapMode.Read);
        return box;
    }

    /// <summary>Reports how large the mapped mip is, which the dynamic-texture upload path uses.</summary>
    public DataBox MapSubresourceWithSize(Resource resource, int mipSlice, int arraySlice, MapMode mode, MapFlags flags, out int mipSize)
    {
        var subresource = Resource.CalculateSubResourceIndex(mipSlice, arraySlice, MipLevelsOf(resource));
        var box = MapSubresource(resource, subresource, mode, flags);
        mipSize = Math.Max(box.SlicePitch, box.RowPitch);
        return box;
    }

    public DataBox MapSubresource(Resource resource, MapMode mode, MapFlags flags, out DataStream stream)
        => MapSubresource(resource, 0, mode, flags, SizeOf(resource), out stream);

    public DataBox MapSubresource(Resource resource, int mipSlice, int arraySlice, MapMode mode, MapFlags flags, out DataStream stream)
    {
        var subresource = Resource.CalculateSubResourceIndex(mipSlice, arraySlice, MipLevelsOf(resource));
        return MapSubresource(resource, subresource, mode, flags, SizeOf(resource), out stream);
    }

    public DataBox MapSubresource(Resource resource, int subresource, MapMode mode, MapFlags flags, int sizeInBytes, out DataStream stream)
    {
        var box = MapSubresource(resource, subresource, mode, flags);
        stream = new DataStream(box.DataPointer, sizeInBytes, mode != MapMode.WriteDiscard, mode != MapMode.Read);
        return box;
    }

    private static int SizeOf(Resource resource)
    {
        return resource switch
                   {
                       Buffer buffer       => buffer.Description.SizeInBytes,
                       Texture2D texture   => texture.Description.Width * texture.Description.Height * 4,
                       Texture3D texture   => texture.Description.Width * texture.Description.Height * texture.Description.Depth * 4,
                       Texture1D texture   => texture.Description.Width * 4,
                       _                   => 0,
                   };
    }

    private static int MipLevelsOf(Resource resource)
    {
        return resource switch
                   {
                       Texture2D texture => Math.Max(1, texture.Description.MipLevels),
                       Texture3D texture => Math.Max(1, texture.Description.MipLevels),
                       Texture1D texture => Math.Max(1, texture.Description.MipLevels),
                       _                 => 1,
                   };
    }

    public void UnmapSubresource(Resource resource, int subresource)
    {
        if (resource.Native != null)
            Backend.Unmap(resource.Native, subresource);
    }

    /// <summary>
    /// Reads a query's result. Always false for now: the backend API has no queries, so a timing operator
    /// reports nothing rather than a made-up number.
    /// </summary>
    public bool GetData<T>(Query query, AsynchronousFlags flags, out T result) where T : struct
    {
        GraphicsLog.WarnOnce("GPU queries are not implemented by the backends, so no timing is reported.");
        result = default;
        return false;
    }

    public void Begin(Query query)
    {
    }

    public void End(Query query)
    {
    }

    /// <summary>
    /// Submits what has been recorded so far. Operators call it before a blocking map; the frame keeps going.
    /// </summary>
    public void Flush()
    {
        EndRendering();
    }
    #endregion

    #region resolving state into pipelines
    private void PrepareDraw()
    {
        BeginRenderingIfNeeded();

        var description = new GraphicsPipelineDescription
                              {
                                  VertexShader = VertexShader.Shader?.GpuShader,
                                  PixelShader = PixelShader.Shader?.GpuShader,
                                  GeometryShader = GeometryShader.Shader?.GpuShader,
                                  VertexLayout = InputAssembler.InputLayout?.Elements,
                                  Topology = Translate.ToTopology(InputAssembler.PrimitiveTopology),
                                  PatchControlPoints = Translate.ToPatchControlPoints(InputAssembler.PrimitiveTopology),
                                  Rasterizer = Rasterizer.State?.State ?? DefaultRasterizerState,
                                  DepthStencil = OutputMerger.DepthStencilState?.State ?? DefaultDepthStencilState,
                                  AlphaToCoverage = OutputMerger.BlendState?.AlphaToCoverage ?? false,
                                  Blend = OutputMerger.BlendStates,
                                  RenderTargetCount = OutputMerger.RenderTargetCount,
                                  RenderTargetFormats = OutputMerger.RenderTargetFormats,
                                  DepthStencilFormat = OutputMerger.DepthStencilFormat,
                                  Samples = OutputMerger.Samples,
                              };

        Commands.SetPipeline(Backend.GetOrCreatePipeline(description));
        Commands.SetBlendConstants(OutputMerger.BlendFactor, OutputMerger.SampleMask);
        Rasterizer.Flush(Commands);

        FlushBindings(VertexShader);
        FlushBindings(PixelShader);
        FlushBindings(GeometryShader);
        InputAssembler.Flush(Commands);
    }

    private bool PrepareDispatch()
    {
        if (ComputeShader.Shader is not { GpuShader: { } shader })
            return false;

        // Compute cannot run inside a render pass.
        EndRendering();
        Commands.SetPipeline(Backend.GetOrCreatePipeline(new ComputePipelineDescription { ComputeShader = shader }));
        FlushBindings(ComputeShader);
        return true;
    }

    private void FlushBindings(ShaderStageState stage)
    {
        // Always sent, even when empty: a stage that bound something for the previous draw has to be
        // cleared, or its descriptors stay live.
        var count = stage.CollectBindings(_bindings);
        Commands.SetBindings(stage.Stage, _bindings.AsSpan(0, count));
    }

    /// <summary>
    /// Dynamic rendering has no framebuffer object: the targets are named when rendering begins, and every
    /// clear or copy has to interrupt it. The context therefore opens the pass lazily, on the first draw.
    /// </summary>
    private void BeginRenderingIfNeeded()
    {
        if (_renderingActive)
            return;

        var count = OutputMerger.CollectRenderTargets(_renderTargets);
        Commands.BeginRendering(_renderTargets.AsSpan(0, count), OutputMerger.DepthStencilTarget?.GpuView);
        _renderingActive = true;
    }

    internal void EndRendering()
    {
        if (!_renderingActive)
            return;

        Commands.EndRendering();
        _renderingActive = false;
    }

    /// <summary>Called by the output merger: new targets mean the current pass has to end.</summary>
    internal void InvalidateRenderTargets() => EndRendering();

    private static readonly T3.Graphics.RasterState DefaultRasterizerState = new()
                                                                         {
                                                                             Fill = T3.Graphics.PolygonMode.Solid,
                                                                             Cull = T3.Graphics.FaceCulling.Back,
                                                                             DepthClip = true,
                                                                         };

    private static readonly T3.Graphics.DepthState DefaultDepthStencilState = new()
                                                                             {
                                                                                 DepthTest = true,
                                                                                 DepthWrite = true,
                                                                                 DepthCompare = CompareFunction.Less,
                                                                                 StencilReadMask = 0xff,
                                                                                 StencilWriteMask = 0xff,
                                                                             };

    private ICommandList? _commands;
    private bool _renderingActive;
    private readonly Binding[] _bindings = new Binding[ShaderStageState.MaxBindingsPerStage];
    private readonly GpuTextureView[] _renderTargets = new GpuTextureView[BlendTargetStates.MaxRenderTargets];
    #endregion
}

/// <summary>What <see cref="DeviceContext.PushState"/> saves. Operators save only what they change.</summary>
[Flags]
public enum StateGroups
{
    None = 0,
    InputAssembler = 1,
    Rasterizer = 2,
    OutputMerger = 4,
    VertexShader = 8,
    PixelShader = 16,
    GeometryShader = 32,
    ComputeShader = 64,
    All = InputAssembler | Rasterizer | OutputMerger | VertexShader | PixelShader | GeometryShader | ComputeShader,
}

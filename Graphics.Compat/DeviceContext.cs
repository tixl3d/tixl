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
        VertexShader = new ShaderStageState(ShaderStage.Vertex);
        PixelShader = new ShaderStageState(ShaderStage.Pixel);
        GeometryShader = new ShaderStageState(ShaderStage.Geometry);
        ComputeShader = new ComputeShaderStage();
    }

    internal readonly IGraphicsBackend Backend;

    public InputAssemblerStage InputAssembler { get; }
    public RasterizerStage Rasterizer { get; }
    public OutputMergerStage OutputMerger { get; }
    public ShaderStageState VertexShader { get; }
    public ShaderStageState PixelShader { get; }
    public ShaderStageState GeometryShader { get; }
    public ComputeShaderStage ComputeShader { get; }

    #region frame
    internal void BeginFrame()
    {
        _commands = Backend.BeginFrame();
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

    private ICommandList Commands => _commands ?? throw new InvalidOperationException("No frame is being recorded. Call Device.BeginFrame first.");
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

    public void UnmapSubresource(Resource resource, int subresource)
    {
        if (resource.Native != null)
            Backend.Unmap(resource.Native, subresource);
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
        Commands.SetBindings((int)stage.Stage, _bindings.AsSpan(0, count));
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

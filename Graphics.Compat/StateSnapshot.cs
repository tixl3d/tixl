using System.Numerics;
using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// A copy of the context state an operator asked to save. This is the shadow stack that replaces D3D11's
/// <c>Get*</c> calls: Vulkan has nothing to read back, and the readback also leaked a COM reference for every
/// object it returned.
/// </summary>
/// <remarks>
/// Snapshots are pooled and reused, so pushing and popping every frame allocates nothing after the first few.
/// </remarks>
internal sealed class StateSnapshot
{
    internal void Capture(DeviceContext context, StateGroups groups)
    {
        _groups = groups;

        if ((groups & StateGroups.InputAssembler) != 0)
            context.InputAssembler.CaptureTo(_inputAssembler);

        if ((groups & StateGroups.Rasterizer) != 0)
        {
            _rasterizerState = context.Rasterizer.State;
            _viewport = context.Rasterizer.Viewport;
            _viewportCount = context.Rasterizer.ViewportCount;
            _scissor = context.Rasterizer.Scissor;
            _hasScissor = context.Rasterizer.HasScissor;
        }

        if ((groups & StateGroups.OutputMerger) != 0)
        {
            Array.Copy(context.OutputMerger.RenderTargets, _renderTargets, _renderTargets.Length);
            _depthStencilTarget = context.OutputMerger.DepthStencilTarget;
            _blendState = context.OutputMerger.BlendState;
            _blendFactor = context.OutputMerger.BlendFactor;
            _sampleMask = context.OutputMerger.SampleMask;
            _depthStencilState = context.OutputMerger.DepthStencilState;
            _stencilReference = context.OutputMerger.StencilReference;
        }

        if ((groups & StateGroups.VertexShader) != 0)
            _vertex.Capture(context.VertexShader);

        if ((groups & StateGroups.PixelShader) != 0)
            _pixel.Capture(context.PixelShader);

        if ((groups & StateGroups.GeometryShader) != 0)
            _geometry.Capture(context.GeometryShader);

        if ((groups & StateGroups.ComputeShader) != 0)
            _compute.Capture(context.ComputeShader);
    }

    internal void Restore(DeviceContext context)
    {
        if ((_groups & StateGroups.InputAssembler) != 0)
            context.InputAssembler.RestoreFrom(_inputAssembler);

        if ((_groups & StateGroups.Rasterizer) != 0)
        {
            context.Rasterizer.State = _rasterizerState;
            context.Rasterizer.Restore(_viewport, _viewportCount, _scissor, _hasScissor);
        }

        if ((_groups & StateGroups.OutputMerger) != 0)
        {
            context.OutputMerger.RestoreTargets(_renderTargets, _depthStencilTarget);
            context.OutputMerger.RestoreStates(_blendState, _blendFactor, _sampleMask, _depthStencilState, _stencilReference);
        }

        if ((_groups & StateGroups.VertexShader) != 0)
            _vertex.Restore(context.VertexShader);

        if ((_groups & StateGroups.PixelShader) != 0)
            _pixel.Restore(context.PixelShader);

        if ((_groups & StateGroups.GeometryShader) != 0)
            _geometry.Restore(context.GeometryShader);

        if ((_groups & StateGroups.ComputeShader) != 0)
            _compute.Restore(context.ComputeShader);
    }

    private StateGroups _groups;

    private readonly InputAssemblerSnapshot _inputAssembler = new();

    private RasterizerState? _rasterizerState;
    private Viewport _viewport;
    private int _viewportCount;
    private ScissorRect _scissor;
    private bool _hasScissor;

    private readonly RenderTargetView?[] _renderTargets = new RenderTargetView?[BlendTargetStates.MaxRenderTargets];
    private DepthStencilView? _depthStencilTarget;
    private BlendState? _blendState;
    private Vector4 _blendFactor;
    private uint _sampleMask;
    private DepthStencilState? _depthStencilState;
    private int _stencilReference;

    private readonly StageSnapshot _vertex = new();
    private readonly StageSnapshot _pixel = new();
    private readonly StageSnapshot _geometry = new();
    private readonly StageSnapshot _compute = new();
}

internal sealed class InputAssemblerSnapshot
{
    internal PrimitiveTopology Topology;
    internal InputLayout? InputLayout;
    internal readonly VertexBufferBinding[] VertexBuffers = new VertexBufferBinding[InputAssemblerStage.MaxVertexBuffers];
    internal Buffer? IndexBuffer;
    internal Format IndexFormat;
    internal int IndexOffset;
}

internal sealed class StageSnapshot
{
    internal void Capture(ShaderStageState stage)
    {
        _shader = stage.Shader;
        Array.Copy(stage.ConstantBuffers, _constantBuffers, _constantBuffers.Length);
        Array.Copy(stage.ShaderResources, _shaderResources, _shaderResources.Length);
        Array.Copy(stage.Samplers, _samplers, _samplers.Length);
        Array.Copy(stage.UnorderedAccessViews, _unorderedAccessViews, _unorderedAccessViews.Length);
        Array.Copy(stage.UnorderedAccessCounts, _unorderedAccessCounts, _unorderedAccessCounts.Length);
    }

    internal void Restore(ShaderStageState stage)
    {
        stage.RestoreShader(_shader);
        Array.Copy(_constantBuffers, stage.ConstantBuffers, _constantBuffers.Length);
        Array.Copy(_shaderResources, stage.ShaderResources, _shaderResources.Length);
        Array.Copy(_samplers, stage.Samplers, _samplers.Length);
        Array.Copy(_unorderedAccessViews, stage.UnorderedAccessViews, _unorderedAccessViews.Length);
        Array.Copy(_unorderedAccessCounts, stage.UnorderedAccessCounts, _unorderedAccessCounts.Length);
    }

    private Shader? _shader;
    private readonly Buffer?[] _constantBuffers = new Buffer?[ShaderStageState.MaxConstantBuffers];
    private readonly ShaderResourceView?[] _shaderResources = new ShaderResourceView?[ShaderStageState.MaxShaderResources];
    private readonly SamplerState?[] _samplers = new SamplerState?[ShaderStageState.MaxSamplers];
    private readonly UnorderedAccessView?[] _unorderedAccessViews = new UnorderedAccessView?[ShaderStageState.MaxUnorderedAccessViews];
    private readonly int[] _unorderedAccessCounts = new int[ShaderStageState.MaxUnorderedAccessViews];
}

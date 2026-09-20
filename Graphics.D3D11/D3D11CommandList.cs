using System.Numerics;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using T3.Graphics;
using NativeBuffer = SharpDX.Direct3D11.Buffer;
using D3D11Device = SharpDX.Direct3D11.Device;

namespace T3.Graphics.D3D11;

/// <summary>
/// Records straight into the immediate context. D3D11 has no command list to fill and no render pass, so
/// "recording" is just calling the context in the order the backend API prescribes.
/// </summary>
internal sealed class D3D11CommandList(D3D11Device device) : ICommandList
{
    internal readonly DeviceContext Context = device.ImmediateContext;

    public void BeginRendering(ReadOnlySpan<GpuTextureView> colorTargets, GpuTextureView? depthTarget)
    {
        var count = 0;
        for (var i = 0; i < colorTargets.Length && i < _renderTargets.Length; i++)
        {
            _renderTargets[i] = (colorTargets[i] as D3D11TextureView)?.RenderTarget;
            count = i + 1;
        }

        for (var i = count; i < _renderTargets.Length; i++)
        {
            _renderTargets[i] = null;
        }

        Context.OutputMerger.SetRenderTargets((depthTarget as D3D11TextureView)?.DepthStencil, _renderTargets);
    }

    /// <summary>Nothing to end: D3D11 keeps the targets bound until they are replaced.</summary>
    public void EndRendering()
    {
    }

    public void Clear(GpuTextureView target, Vector4 color)
    {
        if ((target as D3D11TextureView)?.RenderTarget is { } view)
            Context.ClearRenderTargetView(view, new RawColor4(color.X, color.Y, color.Z, color.W));
    }

    public void ClearDepthStencil(GpuTextureView target, float depth, byte stencil, bool clearDepth, bool clearStencil)
    {
        if ((target as D3D11TextureView)?.DepthStencil is not { } view)
            return;

        var flags = (DepthStencilClearFlags)0;

        if (clearDepth)
            flags |= DepthStencilClearFlags.Depth;

        if (clearStencil)
            flags |= DepthStencilClearFlags.Stencil;

        if (flags != 0)
            Context.ClearDepthStencilView(view, flags, depth, stencil);
    }

    public void SetViewport(in Viewport viewport)
        => Context.Rasterizer.SetViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);

    public void SetScissor(in ScissorRect rect)
        => Context.Rasterizer.SetScissorRectangle(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);

    public void SetBlendConstants(Vector4 factor, uint sampleMask)
    {
        _blendFactor = new RawColor4(factor.X, factor.Y, factor.Z, factor.W);
        _sampleMask = unchecked((int)sampleMask);

        if (_blend != null)
            Context.OutputMerger.SetBlendState(_blend, _blendFactor, _sampleMask);
    }

    public void SetStencilReference(int reference)
    {
        _stencilReference = reference;

        if (_depthStencil != null)
            Context.OutputMerger.SetDepthStencilState(_depthStencil, _stencilReference);
    }

    public void SetPipeline(GpuPipeline pipeline)
    {
        if (pipeline is not D3D11Pipeline d3d11Pipeline)
            return;

        if (d3d11Pipeline.IsCompute)
        {
            Context.ComputeShader.Set(d3d11Pipeline.ComputeShader);
            return;
        }

        Context.VertexShader.Set(d3d11Pipeline.VertexShader);
        Context.PixelShader.Set(d3d11Pipeline.PixelShader);
        Context.GeometryShader.Set(d3d11Pipeline.GeometryShader);
        Context.InputAssembler.InputLayout = d3d11Pipeline.InputLayout;
        Context.InputAssembler.PrimitiveTopology = d3d11Pipeline.Topology;
        Context.Rasterizer.State = d3d11Pipeline.Rasterizer;

        _blend = d3d11Pipeline.Blend;
        _depthStencil = d3d11Pipeline.DepthStencil;
        Context.OutputMerger.SetBlendState(_blend, _blendFactor, _sampleMask);
        Context.OutputMerger.SetDepthStencilState(_depthStencil, _stencilReference);
    }

    public void SetBindings(ShaderStage shaderStage, ReadOnlySpan<Binding> bindings)
    {
        var stage = StageFor(shaderStage);
        if (stage == null)
            return;

        var state = _bindingStates[(int)shaderStage];
        state.BeginUpdate();

        foreach (var binding in bindings)
        {
            switch (binding.Kind)
            {
                case BindingKind.Sampler:
                    state.Sampler(binding.Slot - SamplerBase, (binding.Sampler as D3D11Sampler)?.State);
                    break;

                case BindingKind.ConstantBuffer:
                    state.ConstantBuffer(binding.Slot - ConstantBufferBase, (binding.Buffer as D3D11Buffer)?.Buffer);
                    break;

                case BindingKind.SampledTexture:
                    state.ShaderResource(binding.Slot - ShaderResourceBase, (binding.TextureView as D3D11TextureView)?.ShaderResource);
                    break;

                case BindingKind.StructuredBuffer or BindingKind.StorageBuffer when binding.Slot < UnorderedAccessBase:
                    state.ShaderResource(binding.Slot - ShaderResourceBase,
                                         (binding.Buffer as D3D11Buffer)?.ShaderResourceFor(binding.BufferOffset, binding.BufferSize));
                    break;

                case BindingKind.StorageTexture:
                    state.UnorderedAccess(binding.Slot - UnorderedAccessBase, (binding.TextureView as D3D11TextureView)?.UnorderedAccess,
                                          binding.InitialCount);
                    break;

                case BindingKind.StorageBuffer or BindingKind.StructuredBuffer:
                    state.UnorderedAccess(binding.Slot - UnorderedAccessBase,
                                          (binding.Buffer as D3D11Buffer)?.UnorderedAccessFor(binding.BufferOffset, binding.BufferSize),
                                          binding.InitialCount);
                    break;
            }
        }

        state.Apply(Context, stage, shaderStage);
    }

    public unsafe void SetInlineConstants(ShaderStage shaderStage, int slot, ReadOnlySpan<byte> data)
    {
        // D3D11 has no push constants, so this lands in a dynamic constant buffer that is renamed per write —
        // the same trick the driver uses for Map(WriteDiscard).
        var buffer = InlineConstantBuffer((int)shaderStage, slot, data.Length);
        var box = Context.MapSubresource(buffer, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);
        data.CopyTo(new Span<byte>((void*)box.DataPointer, data.Length));
        Context.UnmapSubresource(buffer, 0);

        var stage = StageFor(shaderStage);
        stage?.SetConstantBuffer(slot, buffer);
    }

    public unsafe MappedMemory MapForDiscard(GpuResource resource, int subresource)
    {
        var target = NativeResource(resource);
        if (target == null)
            return default;

        var box = Context.MapSubresource(target, subresource, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);
        return new MappedMemory((void*)box.DataPointer, box.RowPitch, box.SlicePitch, box.SlicePitch);
    }

    public void Unmap(GpuResource resource, int subresource)
    {
        var target = NativeResource(resource);
        if (target != null)
            Context.UnmapSubresource(target, subresource);
    }

    public unsafe void UpdateResource(GpuResource resource, int subresource, ReadOnlySpan<byte> data, int rowPitch, int slicePitch)
    {
        var target = NativeResource(resource);
        if (target == null || data.IsEmpty)
            return;

        fixed (byte* pointer = data)
        {
            Context.UpdateSubresource(new SharpDX.DataBox((IntPtr)pointer, rowPitch, slicePitch), target, subresource);
        }
    }

    public void SetVertexBuffers(int startSlot, ReadOnlySpan<VertexBufferView> buffers)
    {
        for (var i = 0; i < buffers.Length; i++)
        {
            var buffer = (buffers[i].Buffer as D3D11Buffer)?.Buffer;
            Context.InputAssembler.SetVertexBuffers(startSlot + i, new VertexBufferBinding(buffer, buffers[i].Stride, buffers[i].Offset));
        }
    }

    public void SetIndexBuffer(GpuBuffer? buffer, Format format, int offset)
        => Context.InputAssembler.SetIndexBuffer((buffer as D3D11Buffer)?.Buffer, Convert.ToDxgi(format), offset);

    public void Draw(int vertexCount, int instanceCount, int firstVertex, int firstInstance)
    {
        if (instanceCount == 1 && firstInstance == 0)
            Context.Draw(vertexCount, firstVertex);
        else
            Context.DrawInstanced(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(int indexCount, int instanceCount, int firstIndex, int vertexOffset, int firstInstance)
    {
        if (instanceCount == 1 && firstInstance == 0)
            Context.DrawIndexed(indexCount, firstIndex, vertexOffset);
        else
            Context.DrawIndexedInstanced(indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public void DrawIndirect(GpuBuffer arguments, int offset)
    {
        if ((arguments as D3D11Buffer)?.Buffer is { } buffer)
            Context.DrawInstancedIndirect(buffer, offset);
    }

    public void Dispatch(int groupsX, int groupsY, int groupsZ) => Context.Dispatch(groupsX, groupsY, groupsZ);

    public void DispatchIndirect(GpuBuffer arguments, int offset)
    {
        if ((arguments as D3D11Buffer)?.Buffer is { } buffer)
            Context.DispatchIndirect(buffer, offset);
    }

    public void CopyTexture(GpuTexture source, GpuTexture destination)
    {
        if (source is D3D11Texture from && destination is D3D11Texture to)
            Context.CopyResource(from.Resource, to.Resource);
    }

    public void CopyBuffer(GpuBuffer source, int sourceOffset, GpuBuffer destination, int destinationOffset, int size)
    {
        if (source is not D3D11Buffer from || destination is not D3D11Buffer to)
            return;

        if (sourceOffset == 0 && destinationOffset == 0 && size >= from.Description.SizeInBytes)
        {
            Context.CopyResource(from.Buffer, to.Buffer);
            return;
        }

        Context.CopySubresourceRegion(from.Buffer, 0, new ResourceRegion(sourceOffset, 0, 0, sourceOffset + size, 1, 1),
                                      to.Buffer, 0, destinationOffset);
    }

    public void CopyTextureRegion(GpuTexture source, int sourceSubresource, GpuTexture destination, int destinationSubresource, int x, int y, int z)
    {
        if (source is D3D11Texture from && destination is D3D11Texture to)
            Context.CopySubresourceRegion(from.Resource, sourceSubresource, null, to.Resource, destinationSubresource, x, y, z);
    }

    public void ResolveTexture(GpuTexture source, GpuTexture destination, Format format)
    {
        if (source is D3D11Texture from && destination is D3D11Texture to)
            Context.ResolveSubresource(from.Resource, 0, to.Resource, 0, Convert.ToDxgi(format));
    }

    public void GenerateMips(GpuTextureView view)
    {
        if ((view as D3D11TextureView)?.ShaderResource is { } shaderResource)
            Context.GenerateMips(shaderResource);
    }

    public void PushDebugGroup(string name) => _annotation?.BeginEvent(name);

    public void PopDebugGroup() => _annotation?.EndEvent();

    private CommonShaderStage? StageFor(ShaderStage stage)
    {
        return stage switch
                   {
                       ShaderStage.Vertex   => Context.VertexShader,
                       ShaderStage.Pixel    => Context.PixelShader,
                       ShaderStage.Geometry => Context.GeometryShader,
                       ShaderStage.Compute  => Context.ComputeShader,
                       _                    => null,
                   };
    }

    private NativeBuffer InlineConstantBuffer(int set, int slot, int size)
    {
        var key = (set, slot);
        if (_inlineConstants.TryGetValue(key, out var buffer) && buffer.Description.SizeInBytes >= size)
            return buffer;

        buffer?.Dispose();

        // Constant buffers are allocated in 16-byte registers.
        var rounded = (size + 15) / 16 * 16;
        buffer = new NativeBuffer(device,
                                 new BufferDescription(rounded, ResourceUsage.Dynamic, BindFlags.ConstantBuffer,
                                                       CpuAccessFlags.Write, ResourceOptionFlags.None, 0));
        _inlineConstants[key] = buffer;
        return buffer;
    }

    private static Resource? NativeResource(GpuResource resource)
    {
        return resource switch
                   {
                       D3D11Texture texture => texture.Resource,
                       D3D11Buffer buffer   => buffer.Buffer,
                       _                    => null,
                   };
    }

    internal void ReleaseInlineConstants()
    {
        foreach (var buffer in _inlineConstants.Values)
        {
            buffer.Dispose();
        }

        _inlineConstants.Clear();
    }

    // The slot bases the shaders are compiled with, undone here.
    private const int SamplerBase = ShaderSlots.SamplerBase;
    private const int ConstantBufferBase = ShaderSlots.ConstantBufferBase;
    private const int ShaderResourceBase = ShaderSlots.ShaderResourceBase;
    private const int UnorderedAccessBase = ShaderSlots.UnorderedAccessBase;

    private readonly RenderTargetView?[] _renderTargets = new RenderTargetView?[BlendTargetStates.MaxRenderTargets];
    private readonly StageBindings[] _bindingStates = [new(), new(), new(), new()];
    private readonly Dictionary<(int, int), NativeBuffer> _inlineConstants = [];
    private readonly UserDefinedAnnotation? _annotation = device.ImmediateContext.QueryInterfaceOrNull<UserDefinedAnnotation>();

    private BlendState? _blend;
    private DepthStencilState? _depthStencil;
    private RawColor4 _blendFactor = new(1, 1, 1, 1);
    private int _sampleMask = unchecked((int)0xffffffff);
    private int _stencilReference;
}

/// <summary>
/// What one stage currently has bound. The backend API sends a stage's whole binding set per draw, so this
/// diffs it against what the context already holds: it unbinds what disappeared — which D3D11 would otherwise
/// leave bound and trip over the next time the resource is written — and touches nothing that did not change.
/// </summary>
internal sealed class StageBindings
{
    internal void BeginUpdate()
    {
        Array.Clear(_samplers);
        Array.Clear(_constantBuffers);
        Array.Clear(_shaderResources);
        Array.Clear(_unorderedAccessViews);
        Array.Fill(_counts, -1);
    }

    internal void Sampler(int slot, SamplerState? state)
    {
        if (InRange(slot, _samplers.Length))
            _samplers[slot] = state;
    }

    internal void ConstantBuffer(int slot, NativeBuffer? buffer)
    {
        if (InRange(slot, _constantBuffers.Length))
            _constantBuffers[slot] = buffer;
    }

    internal void ShaderResource(int slot, ShaderResourceView? view)
    {
        if (InRange(slot, _shaderResources.Length))
            _shaderResources[slot] = view;
    }

    internal void UnorderedAccess(int slot, UnorderedAccessView? view, int initialCount)
    {
        if (!InRange(slot, _unorderedAccessViews.Length))
            return;

        _unorderedAccessViews[slot] = view;
        _counts[slot] = initialCount;
    }

    internal void Apply(DeviceContext context, CommonShaderStage stage, ShaderStage which)
    {
        for (var slot = 0; slot < _samplers.Length; slot++)
        {
            if (Changed(ref _appliedSamplers[slot], _samplers[slot]))
                stage.SetSampler(slot, _samplers[slot]);
        }

        for (var slot = 0; slot < _constantBuffers.Length; slot++)
        {
            if (Changed(ref _appliedConstantBuffers[slot], _constantBuffers[slot]))
                stage.SetConstantBuffer(slot, _constantBuffers[slot]);
        }

        for (var slot = 0; slot < _shaderResources.Length; slot++)
        {
            if (Changed(ref _appliedShaderResources[slot], _shaderResources[slot]))
                stage.SetShaderResource(slot, _shaderResources[slot]);
        }

        for (var slot = 0; slot < _unorderedAccessViews.Length; slot++)
        {
            if (!Changed(ref _appliedUnorderedAccessViews[slot], _unorderedAccessViews[slot]))
                continue;

            // Only compute has unordered access views of its own; a pixel shader's go through the output
            // merger, because D3D11 binds them together with the render targets.
            if (which == ShaderStage.Compute)
                context.ComputeShader.SetUnorderedAccessView(slot, _unorderedAccessViews[slot], _counts[slot]);
            else if (which == ShaderStage.Pixel)
                context.OutputMerger.SetUnorderedAccessView(slot, _unorderedAccessViews[slot], _counts[slot]);
        }
    }

    private static bool Changed<T>(ref T? applied, T? current) where T : class
    {
        if (ReferenceEquals(applied, current))
            return false;

        applied = current;
        return true;
    }

    private static bool InRange(int slot, int length) => slot >= 0 && slot < length;

    private const int MaxConstantBuffers = 14;
    private const int MaxShaderResources = 128;
    private const int MaxSamplers = 16;
    private const int MaxUnorderedAccessViews = 8;

    private readonly SamplerState?[] _samplers = new SamplerState?[MaxSamplers];
    private readonly NativeBuffer?[] _constantBuffers = new NativeBuffer?[MaxConstantBuffers];
    private readonly ShaderResourceView?[] _shaderResources = new ShaderResourceView?[MaxShaderResources];
    private readonly UnorderedAccessView?[] _unorderedAccessViews = new UnorderedAccessView?[MaxUnorderedAccessViews];
    private readonly int[] _counts = new int[MaxUnorderedAccessViews];

    private readonly SamplerState?[] _appliedSamplers = new SamplerState?[MaxSamplers];
    private readonly NativeBuffer?[] _appliedConstantBuffers = new NativeBuffer?[MaxConstantBuffers];
    private readonly ShaderResourceView?[] _appliedShaderResources = new ShaderResourceView?[MaxShaderResources];
    private readonly UnorderedAccessView?[] _appliedUnorderedAccessViews = new UnorderedAccessView?[MaxUnorderedAccessViews];
}

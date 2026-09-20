using System.Numerics;
using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// One shader stage of the immediate context. D3D11 gives every stage its own slot space, so the shadow state
/// does too; the bindings are translated into the backend's set-per-stage layout when a draw needs them.
/// </summary>
public class ShaderStageState
{
    internal ShaderStageState(ShaderStage stage)
    {
        Stage = stage;
    }

    internal readonly ShaderStage Stage;

    public Shader? Shader { get; private set; }

    public void Set(Shader? shader) => Shader = shader;

    public void SetConstantBuffer(int slot, Buffer? buffer) => Assign(ConstantBuffers, slot, buffer, "constant buffer");

    public void SetConstantBuffers(int startSlot, params Buffer?[] buffers) => AssignRange(ConstantBuffers, startSlot, buffers, "constant buffer");

    public void SetConstantBuffers(int startSlot, int count, Buffer?[] buffers)
        => AssignRange(ConstantBuffers, startSlot, buffers.AsSpan(0, Math.Min(count, buffers.Length)), "constant buffer");

    public void SetShaderResource(int slot, ShaderResourceView? view) => Assign(ShaderResources, slot, view, "shader resource");

    public void SetShaderResources(int startSlot, params ShaderResourceView?[] views) => AssignRange(ShaderResources, startSlot, views, "shader resource");

    public void SetShaderResources(int startSlot, int count, ShaderResourceView?[] views)
        => AssignRange(ShaderResources, startSlot, views.AsSpan(0, Math.Min(count, views.Length)), "shader resource");

    public void SetSampler(int slot, SamplerState? sampler) => Assign(Samplers, slot, sampler, "sampler");

    public void SetSamplers(int startSlot, params SamplerState?[] samplers) => AssignRange(Samplers, startSlot, samplers, "sampler");

    public void SetSamplers(int startSlot, int count, SamplerState?[] samplers)
        => AssignRange(Samplers, startSlot, samplers.AsSpan(0, Math.Min(count, samplers.Length)), "sampler");

    internal void AssignUnorderedAccessView(int slot, UnorderedAccessView? view, int initialCount = -1)
    {
        Assign(UnorderedAccessViews, slot, view, "unordered access view");

        if (slot >= 0 && slot < UnorderedAccessCounts.Length)
            UnorderedAccessCounts[slot] = initialCount;
    }

    internal void RestoreShader(Shader? shader) => Shader = shader;

    internal void Clear()
    {
        Shader = null;
        Array.Clear(ConstantBuffers);
        Array.Clear(ShaderResources);
        Array.Clear(Samplers);
        Array.Clear(UnorderedAccessViews);
        Array.Fill(UnorderedAccessCounts, -1);
    }

    /// <summary>
    /// Writes the stage's bindings into <paramref name="target"/> and returns how many there are. Disposed
    /// views bind as null, which D3D11 did implicitly and Vulkan does through its null descriptors — operators
    /// rely on it, because a resource can die between the operator that set it and the draw that uses it.
    /// </summary>
    internal int CollectBindings(Binding[] target)
    {
        var count = 0;

        for (var slot = 0; slot < Samplers.Length; slot++)
        {
            var sampler = Samplers[slot];
            if (sampler is not { IsDisposed: false })
                continue;

            target[count++] = new Binding { Kind = BindingKind.Sampler, Slot = SamplerBase + slot, Sampler = sampler.GpuSampler };
        }

        for (var slot = 0; slot < ConstantBuffers.Length; slot++)
        {
            var buffer = ConstantBuffers[slot];
            if (buffer is not { IsDisposed: false })
                continue;

            target[count++] = new Binding { Kind = BindingKind.ConstantBuffer, Slot = ConstantBufferBase + slot, Buffer = buffer.GpuBuffer };
        }

        for (var slot = 0; slot < ShaderResources.Length; slot++)
        {
            var view = ShaderResources[slot];
            if (view is not { IsDisposed: false })
                continue;

            target[count++] = ToBinding(view, ShaderResourceBase + slot);
        }

        for (var slot = 0; slot < UnorderedAccessViews.Length; slot++)
        {
            var view = UnorderedAccessViews[slot];
            if (view is not { IsDisposed: false })
                continue;

            target[count++] = ToBinding(view, UnorderedAccessBase + slot, UnorderedAccessCounts[slot]);
        }

        return count;
    }

    private static Binding ToBinding(ShaderResourceView view, int slot)
    {
        if (view.GpuView != null)
            return new Binding { Kind = BindingKind.SampledTexture, Slot = slot, TextureView = view.GpuView };

        var buffer = (Buffer)view.ViewedResource;
        var structured = (buffer.Description.OptionFlags & ResourceOptionFlags.BufferStructured) != 0;
        var (offset, size) = ViewDescriptions.BufferRange(buffer, view.Description.BufferEx.FirstElement, view.Description.BufferEx.ElementCount);

        return new Binding
                   {
                       Kind = structured ? BindingKind.StructuredBuffer : BindingKind.StorageBuffer,
                       Slot = slot,
                       Buffer = buffer.GpuBuffer,
                       BufferOffset = offset,
                       BufferSize = size,
                   };
    }

    private static Binding ToBinding(UnorderedAccessView view, int slot, int initialCount)
    {
        if (view.GpuView != null)
            return new Binding { Kind = BindingKind.StorageTexture, Slot = slot, TextureView = view.GpuView, InitialCount = initialCount };

        var buffer = (Buffer)view.ViewedResource;
        var (offset, size) = ViewDescriptions.BufferRange(buffer, view.Description.Buffer.FirstElement, view.Description.Buffer.ElementCount);

        return new Binding
                   {
                       Kind = BindingKind.StorageBuffer,
                       Slot = slot,
                       Buffer = buffer.GpuBuffer,
                       BufferOffset = offset,
                       BufferSize = size,
                       InitialCount = initialCount,
                   };
    }

    private void Assign<T>(T?[] slots, int slot, T? value, string what) where T : class
    {
        if (slot < 0 || slot >= slots.Length)
        {
            // Slot counts come from multi-input array lengths and were never clamped; D3D11 ignored the
            // overflow, Vulkan would not.
            GraphicsLog.WarnOnce($"{Stage} {what} slot {slot} is outside the {slots.Length} slots D3D11 allows and was ignored.");
            return;
        }

        slots[slot] = value;
    }

    private void AssignRange<T>(T?[] slots, int startSlot, ReadOnlySpan<T?> values, string what) where T : class
    {
        for (var i = 0; i < values.Length; i++)
        {
            Assign(slots, startSlot + i, values[i], what);
        }
    }

    // D3D11 slot limits. Anything beyond them was already invalid; it just failed silently.
    internal const int MaxConstantBuffers = 14;
    internal const int MaxShaderResources = 128;
    internal const int MaxSamplers = 16;
    internal const int MaxUnorderedAccessViews = 8;
    internal const int MaxBindingsPerStage = MaxConstantBuffers + MaxShaderResources + MaxSamplers + MaxUnorderedAccessViews;

    // Register shifts, matching how the shaders are compiled (s→0, b→16, t→32, u→160).
    private const int SamplerBase = 0;
    private const int ConstantBufferBase = 16;
    private const int ShaderResourceBase = 32;
    private const int UnorderedAccessBase = 160;

    internal readonly Buffer?[] ConstantBuffers = new Buffer?[MaxConstantBuffers];
    internal readonly ShaderResourceView?[] ShaderResources = new ShaderResourceView?[MaxShaderResources];
    internal readonly SamplerState?[] Samplers = new SamplerState?[MaxSamplers];
    internal readonly UnorderedAccessView?[] UnorderedAccessViews = new UnorderedAccessView?[MaxUnorderedAccessViews];
    internal readonly int[] UnorderedAccessCounts = CreateCounts();

    private static int[] CreateCounts()
    {
        var counts = new int[MaxUnorderedAccessViews];
        Array.Fill(counts, -1);
        return counts;
    }
}

public sealed class ComputeShaderStage() : ShaderStageState(ShaderStage.Compute)
{
    /// <summary>A count of -1 leaves an append or counter buffer's counter alone, as in D3D11.</summary>
    public void SetUnorderedAccessView(int slot, UnorderedAccessView? view, int initialCount = -1)
        => AssignUnorderedAccessView(slot, view, initialCount);

    public void SetUnorderedAccessViews(int startSlot, params UnorderedAccessView?[] views)
    {
        for (var i = 0; i < views.Length; i++)
        {
            AssignUnorderedAccessView(startSlot + i, views[i]);
        }
    }

    public void SetUnorderedAccessViews(int startSlot, UnorderedAccessView?[] views, int[] initialCounts)
    {
        for (var i = 0; i < views.Length; i++)
        {
            AssignUnorderedAccessView(startSlot + i, views[i], i < initialCounts.Length ? initialCounts[i] : -1);
        }
    }
}

public sealed class InputAssemblerStage
{
    public PrimitiveTopology PrimitiveTopology { get; set; } = PrimitiveTopology.TriangleList;
    public InputLayout? InputLayout { get; set; }

    public void SetVertexBuffers(int slot, params VertexBufferBinding[] bindings)
    {
        for (var i = 0; i < bindings.Length && slot + i < _vertexBuffers.Length; i++)
        {
            _vertexBuffers[slot + i] = bindings[i];
        }
    }

    public void SetIndexBuffer(Buffer? buffer, Format format, int offset)
    {
        _indexBuffer = buffer;
        _indexFormat = format;
        _indexOffset = offset;
    }

    internal void Flush(ICommandList commands)
    {
        var count = 0;
        for (var i = 0; i < _vertexBuffers.Length; i++)
        {
            var buffer = _vertexBuffers[i].Buffer;
            _nativeBuffers[i] = new T3.Graphics.VertexBufferView(buffer?.GpuBuffer, _vertexBuffers[i].Stride, _vertexBuffers[i].Offset);

            if (buffer != null)
                count = i + 1;
        }

        if (count > 0)
            commands.SetVertexBuffers(0, _nativeBuffers.AsSpan(0, count));

        if (_indexBuffer != null)
            commands.SetIndexBuffer(_indexBuffer.GpuBuffer, _indexFormat, _indexOffset);
    }

    internal void CaptureTo(InputAssemblerSnapshot snapshot)
    {
        snapshot.Topology = PrimitiveTopology;
        snapshot.InputLayout = InputLayout;
        Array.Copy(_vertexBuffers, snapshot.VertexBuffers, _vertexBuffers.Length);
        snapshot.IndexBuffer = _indexBuffer;
        snapshot.IndexFormat = _indexFormat;
        snapshot.IndexOffset = _indexOffset;
    }

    internal void RestoreFrom(InputAssemblerSnapshot snapshot)
    {
        PrimitiveTopology = snapshot.Topology;
        InputLayout = snapshot.InputLayout;
        Array.Copy(snapshot.VertexBuffers, _vertexBuffers, _vertexBuffers.Length);
        _indexBuffer = snapshot.IndexBuffer;
        _indexFormat = snapshot.IndexFormat;
        _indexOffset = snapshot.IndexOffset;
    }

    internal void Clear()
    {
        PrimitiveTopology = PrimitiveTopology.TriangleList;
        InputLayout = null;
        Array.Clear(_vertexBuffers);
        _indexBuffer = null;
    }

    internal const int MaxVertexBuffers = 8;
    private readonly VertexBufferBinding[] _vertexBuffers = new VertexBufferBinding[MaxVertexBuffers];
    private readonly T3.Graphics.VertexBufferView[] _nativeBuffers = new T3.Graphics.VertexBufferView[MaxVertexBuffers];
    private Buffer? _indexBuffer;
    private Format _indexFormat = Format.R32_UInt;
    private int _indexOffset;
}

public sealed class RasterizerStage
{
    public RasterizerState? State { get; set; }

    public void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f)
        => SetViewport(new Viewport(x, y, width, height, minDepth, maxDepth));

    public void SetViewport(in Viewport viewport)
    {
        _viewport = viewport;
        _viewportCount = 1;
    }

    public void SetViewports(ReadOnlySpan<Viewport> viewports)
    {
        if (viewports.Length == 0)
        {
            _viewportCount = 0;
            return;
        }

        // TiXL never renders to more than one viewport; multi-viewport would need the pipeline to declare it.
        if (viewports.Length > 1)
            GraphicsLog.WarnOnce("Only the first viewport is used; multiple viewports are not supported.");

        SetViewport(viewports[0]);
    }

    public void SetScissorRectangle(int left, int top, int right, int bottom)
    {
        _scissor = new ScissorRect(left, top, right - left, bottom - top);
        _hasScissor = true;
    }

    public Viewport GetViewport() => _viewport;

    internal void Flush(ICommandList commands)
    {
        if (_viewportCount > 0)
            commands.SetViewport(_viewport);

        // A pipeline with scissor disabled still needs a rectangle in Vulkan; the viewport is the natural one.
        commands.SetScissor(_hasScissor
                                ? _scissor
                                : new ScissorRect((int)_viewport.X, (int)_viewport.Y, (int)_viewport.Width, (int)_viewport.Height));
    }

    internal void Clear()
    {
        State = null;
        _viewportCount = 0;
        _hasScissor = false;
    }

    internal Viewport Viewport => _viewport;
    internal ScissorRect Scissor => _scissor;
    internal bool HasScissor => _hasScissor;
    internal int ViewportCount => _viewportCount;

    internal void Restore(in Viewport viewport, int viewportCount, in ScissorRect scissor, bool hasScissor)
    {
        _viewport = viewport;
        _viewportCount = viewportCount;
        _scissor = scissor;
        _hasScissor = hasScissor;
    }

    private Viewport _viewport;
    private int _viewportCount;
    private ScissorRect _scissor;
    private bool _hasScissor;
}

public sealed class OutputMergerStage
{
    internal OutputMergerStage(DeviceContext owner)
    {
        _owner = owner;
    }

    public BlendState? BlendState { get; private set; }
    public DepthStencilState? DepthStencilState { get; private set; }
    public DepthStencilView? DepthStencilTarget { get; private set; }

    public void SetTargets(DepthStencilView? depthStencil, params RenderTargetView?[] renderTargets)
    {
        _owner.InvalidateRenderTargets();
        DepthStencilTarget = depthStencil;
        Array.Clear(_renderTargets);

        for (var i = 0; i < renderTargets.Length && i < _renderTargets.Length; i++)
        {
            _renderTargets[i] = renderTargets[i];
        }
    }

    public void SetTargets(params RenderTargetView?[] renderTargets) => SetTargets(null, renderTargets);

    public void SetRenderTargets(DepthStencilView? depthStencil, params RenderTargetView?[] renderTargets)
        => SetTargets(depthStencil, renderTargets);

    public void SetRenderTargets(params RenderTargetView?[] renderTargets) => SetTargets(null, renderTargets);

    /// <summary>Pixel-shader UAVs. They share the pixel stage's binding set, as they share its slot space in D3D11.</summary>
    public void SetUnorderedAccessViews(int startSlot, params UnorderedAccessView?[] views)
    {
        for (var i = 0; i < views.Length; i++)
        {
            _owner.PixelShader.AssignUnorderedAccessView(startSlot + i, views[i]);
        }
    }

    public void SetBlendState(BlendState? blendState, Vector4? blendFactor = null, uint sampleMask = 0xffffffff)
    {
        BlendState = blendState;
        BlendFactor = blendFactor ?? Vector4.One;
        SampleMask = sampleMask;
    }

    public void SetDepthStencilState(DepthStencilState? depthStencilState, int stencilReference = 0)
    {
        DepthStencilState = depthStencilState;
        StencilReference = stencilReference;
    }

    public Vector4 BlendFactor { get; private set; } = Vector4.One;
    public uint SampleMask { get; private set; } = 0xffffffff;
    public int StencilReference { get; private set; }

    internal int CollectRenderTargets(GpuTextureView[] target)
    {
        var count = 0;
        foreach (var view in _renderTargets)
        {
            if (view?.GpuView == null)
                continue;

            target[count++] = view.GpuView;
        }

        return count;
    }

    internal int RenderTargetCount
    {
        get
        {
            var count = 0;
            foreach (var view in _renderTargets)
            {
                if (view is { IsDisposed: false })
                    count++;
            }

            return count;
        }
    }

    /// <summary>The formats the pipeline is built against; dynamic rendering needs them at pipeline creation.</summary>
    internal FormatSet RenderTargetFormats
    {
        get
        {
            var formats = new FormatSet();
            for (var i = 0; i < _renderTargets.Length; i++)
            {
                formats[i] = _renderTargets[i]?.Description.Format ?? Format.Unknown;
            }

            return formats;
        }
    }

    internal Format DepthStencilFormat => DepthStencilTarget?.Description.Format ?? Format.Unknown;

    internal SampleDescription Samples
    {
        get
        {
            foreach (var view in _renderTargets)
            {
                if (view?.ViewedResource is Texture2D texture)
                    return texture.Description.SampleDescription.Count == 0 ? new SampleDescription(1, 0) : texture.Description.SampleDescription;
            }

            return new SampleDescription(1, 0);
        }
    }

    internal BlendTargetStates BlendStates
    {
        get
        {
            if (BlendState != null)
                return BlendState.States;

            // D3D11's default: no blending, all channels written.
            var states = new BlendTargetStates();
            for (var i = 0; i < BlendTargetStates.MaxRenderTargets; i++)
            {
                states[i] = new BlendTargetState { WriteMask = ColorComponents.All };
            }

            return states;
        }
    }

    internal void Clear()
    {
        _owner.InvalidateRenderTargets();
        Array.Clear(_renderTargets);
        DepthStencilTarget = null;
        BlendState = null;
        DepthStencilState = null;
        BlendFactor = Vector4.One;
        SampleMask = 0xffffffff;
        StencilReference = 0;
    }

    internal RenderTargetView?[] RenderTargets => _renderTargets;

    private readonly RenderTargetView?[] _renderTargets = new RenderTargetView?[BlendTargetStates.MaxRenderTargets];
    private readonly DeviceContext _owner;

    internal void RestoreTargets(RenderTargetView?[] renderTargets, DepthStencilView? depthStencil)
    {
        _owner.InvalidateRenderTargets();
        Array.Copy(renderTargets, _renderTargets, _renderTargets.Length);
        DepthStencilTarget = depthStencil;
    }

    internal void RestoreStates(BlendState? blendState, Vector4 blendFactor, uint sampleMask, DepthStencilState? depthStencilState, int stencilReference)
    {
        BlendState = blendState;
        BlendFactor = blendFactor;
        SampleMask = sampleMask;
        DepthStencilState = depthStencilState;
        StencilReference = stencilReference;
    }
}

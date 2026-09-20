using System.Numerics;
using T3.Graphics;
using T3.Graphics.Compat;
using Xunit;
using Buffer = T3.Graphics.Compat.Buffer;
using Format = T3.Graphics.Format;

namespace Core.Tests;

/// <summary>
/// Checks what the compatibility layer promises operators: the D3D11 state machine turns into pipelines and
/// bindings, and the push/pop stack restores what an operator saved. Runs against a fake backend, so it needs
/// no GPU and covers both platforms.
/// </summary>
public class GraphicsCompatTests
{
    [Fact]
    public void BindingsUseTheShaderRegisterShifts()
    {
        var (backend, device) = CreateDevice();
        var context = device.ImmediateContext;
        device.BeginFrame();

        var texture = RenderTarget(device);
        context.PixelShader.Set(new PixelShader(device, null));
        context.PixelShader.SetShaderResource(0, new ShaderResourceView(device, texture));
        context.PixelShader.SetConstantBuffer(1, ConstantBuffer(device));
        context.PixelShader.SetSampler(2, new SamplerState(device, PointSampler));
        context.OutputMerger.SetTargets(new RenderTargetView(device, texture));
        context.Draw(3, 0);

        var bindings = backend.Commands.BindingsPerStage[ShaderStage.Pixel];
        Assert.Equal(2, Slot(bindings, BindingKind.Sampler));
        Assert.Equal(16 + 1, Slot(bindings, BindingKind.ConstantBuffer));
        Assert.Equal(32 + 0, Slot(bindings, BindingKind.SampledTexture));
    }

    [Fact]
    public void EachShaderStageKeepsItsOwnSlotSpace()
    {
        var (backend, device) = CreateDevice();
        var context = device.ImmediateContext;
        device.BeginFrame();

        var texture = RenderTarget(device);

        // The same D3D11 slot in two stages: without a per-stage slot space one would overwrite the other.
        context.VertexShader.SetShaderResource(0, new ShaderResourceView(device, texture));
        context.PixelShader.SetShaderResource(0, new ShaderResourceView(device, texture));
        context.OutputMerger.SetTargets(new RenderTargetView(device, texture));
        context.Draw(3, 0);

        Assert.Single(backend.Commands.BindingsPerStage[ShaderStage.Vertex]);
        Assert.Single(backend.Commands.BindingsPerStage[ShaderStage.Pixel]);
    }

    [Fact]
    public void DisposedViewsAreNotBound()
    {
        var (backend, device) = CreateDevice();
        var context = device.ImmediateContext;
        device.BeginFrame();

        var texture = RenderTarget(device);
        var view = new ShaderResourceView(device, texture);
        context.PixelShader.SetShaderResource(0, view);
        context.OutputMerger.SetTargets(new RenderTargetView(device, texture));

        // An upstream operator may dispose a view after a downstream one bound it. Under D3D11 that bound a
        // null pointer instead of crashing, and operators rely on it.
        view.Dispose();
        context.Draw(3, 0);

        Assert.Empty(backend.Commands.BindingsPerStage[ShaderStage.Pixel]);
    }

    [Fact]
    public void PushAndPopRestoreStateSetByAnotherOperator()
    {
        var (_, device) = CreateDevice();
        var context = device.ImmediateContext;
        device.BeginFrame();

        var first = new RenderTargetView(device, RenderTarget(device));
        var second = new RenderTargetView(device, RenderTarget(device));
        var sampler = new SamplerState(device, PointSampler);

        context.OutputMerger.SetTargets(first);
        context.PixelShader.SetSampler(0, sampler);

        // What an operator's Update does before it changes state...
        context.PushState(StateGroups.OutputMerger | StateGroups.PixelShader);
        context.OutputMerger.SetTargets(second);
        context.PixelShader.SetSampler(0, null);

        // ...and what the enclosing operator's RestoreAction does later.
        context.PopState();

        Assert.Equal(1, context.OutputMerger.RenderTargetCount);
        Assert.Same(first, context.OutputMerger.RenderTargets[0]);
        Assert.Same(sampler, context.PixelShader.Samplers[0]);
    }

    [Fact]
    public void AnUnbalancedPopIsReportedAndIgnored()
    {
        var (_, device) = CreateDevice();
        var warnings = new List<string>();
        GraphicsLog.Warning = warnings.Add;

        device.ImmediateContext.PopState();

        Assert.Contains(warnings, warning => warning.Contains("PopState"));
        GraphicsLog.Warning = null;
    }

    [Fact]
    public void SamplerDescriptionsTranslateToTheBackend()
    {
        var (backend, device) = CreateDevice();

        _ = new SamplerState(device, PointSampler);
        _ = new SamplerState(device,
                             new SamplerStateDescription
                                 {
                                     Filter = Filter.Anisotropic,
                                     AddressU = TextureAddressMode.Mirror,
                                     AddressV = TextureAddressMode.Clamp,
                                     AddressW = TextureAddressMode.Border,
                                     MaximumAnisotropy = 8,
                                     ComparisonFunction = Comparison.Always,
                                 });

        var point = backend.Samplers[0];
        Assert.Equal(FilterMode.Nearest, point.MinFilter);
        Assert.Equal(FilterMode.Nearest, point.MagFilter);
        Assert.Null(point.Compare);

        var anisotropic = backend.Samplers[1];
        Assert.Equal(FilterMode.Linear, anisotropic.MinFilter);
        Assert.Equal(8, anisotropic.MaxAnisotropy);
        Assert.Equal(AddressMode.MirrorRepeat, anisotropic.AddressU);
        Assert.Equal(AddressMode.ClampToEdge, anisotropic.AddressV);
        Assert.Equal(AddressMode.ClampToBorder, anisotropic.AddressW);
    }

    [Fact]
    public void AComparisonFilterBecomesADepthCompare()
    {
        var (backend, device) = CreateDevice();

        _ = new SamplerState(device,
                             new SamplerStateDescription
                                 {
                                     Filter = Filter.ComparisonMinMagMipLinear,
                                     ComparisonFunction = Comparison.LessEqual,
                                 });

        Assert.Equal(CompareFunction.LessEqual, backend.Samplers[0].Compare);
        Assert.Equal(FilterMode.Linear, backend.Samplers[0].MinFilter);
    }

    [Fact]
    public void ATextureIsCreatedWithTheUsageItsBindFlagsImply()
    {
        var (backend, device) = CreateDevice();

        _ = RenderTarget(device);

        var description = backend.Textures[0];
        Assert.True(description.Usage.HasFlag(TextureUsage.RenderTarget));
        Assert.True(description.Usage.HasFlag(TextureUsage.Sampled));

        // Operators copy between arbitrary resources, so copying is always allowed.
        Assert.True(description.Usage.HasFlag(TextureUsage.CopySource));
        Assert.Equal(MemoryKind.DeviceLocal, description.Memory);
    }

    [Fact]
    public void AStagingTextureIsReadbackMemory()
    {
        var (backend, device) = CreateDevice();

        _ = new Texture2D(device,
                          new Texture2DDescription
                              {
                                  Width = 4,
                                  Height = 4,
                                  Format = Format.R8G8B8A8_UNorm,
                                  Usage = ResourceUsage.Staging,
                                  CpuAccessFlags = CpuAccessFlags.Read,
                              });

        Assert.Equal(MemoryKind.Readback, backend.Textures[0].Memory);
    }

    [Fact]
    public void ThePipelineIsBuiltFromTheBoundTargetsAndState()
    {
        var (backend, device) = CreateDevice();
        var context = device.ImmediateContext;
        device.BeginFrame();

        var texture = RenderTarget(device);
        context.OutputMerger.SetTargets(new RenderTargetView(device, texture));
        context.Rasterizer.State = new RasterizerState(device,
                                                       new RasterizerStateDescription
                                                           {
                                                               FillMode = FillMode.Wireframe,
                                                               CullMode = CullMode.None,
                                                               IsScissorEnabled = true,
                                                           });
        context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineStrip;
        context.Draw(2, 0);

        var pipeline = Assert.Single(backend.GraphicsPipelines);
        Assert.Equal(T3.Graphics.PolygonMode.Wireframe, pipeline.Rasterizer.Fill);
        Assert.Equal(T3.Graphics.FaceCulling.None, pipeline.Rasterizer.Cull);
        Assert.True(pipeline.Rasterizer.Scissor);
        Assert.Equal(T3.Graphics.Topology.LineStrip, pipeline.Topology);
        Assert.Equal(1, pipeline.RenderTargetCount);
        Assert.Equal(Format.R8G8B8A8_UNorm, pipeline.RenderTargetFormats[0]);

        // Dynamic rendering opens the pass on the first draw, not when the targets are set.
        Assert.Equal(1, backend.Commands.RenderingBegun);
    }

    [Fact]
    public void ADispatchNeedsNoRenderPass()
    {
        var (backend, device) = CreateDevice();
        var context = device.ImmediateContext;
        device.BeginFrame();

        context.ComputeShader.Set(new T3.Graphics.Compat.ComputeShader(device, backend.CreateShader(ShaderStage.Compute, default, "main")));
        context.Dispatch(1, 1, 1);

        Assert.Equal(0, backend.Commands.RenderingBegun);
        Assert.Equal(1, backend.Commands.Dispatches);
    }

    [Fact]
    public void ADispatchWithoutAShaderDoesNothing()
    {
        var (backend, device) = CreateDevice();
        device.BeginFrame();

        device.ImmediateContext.Dispatch(1, 1, 1);

        Assert.Equal(0, backend.Commands.Dispatches);
    }

    [Fact]
    public void SlotsBeyondTheD3D11LimitsAreClampedAndReported()
    {
        var (backend, device) = CreateDevice();
        var context = device.ImmediateContext;
        var warnings = new List<string>();
        GraphicsLog.Warning = warnings.Add;
        device.BeginFrame();

        var texture = RenderTarget(device);

        // Slot counts come from multi-input arrays, which nothing clamped before.
        context.PixelShader.SetShaderResource(500, new ShaderResourceView(device, texture));
        context.OutputMerger.SetTargets(new RenderTargetView(device, texture));
        context.Draw(3, 0);

        Assert.Empty(backend.Commands.BindingsPerStage[ShaderStage.Pixel]);
        Assert.Contains(warnings, warning => warning.Contains("500"));
        GraphicsLog.Warning = null;
    }

    private static (FakeGraphicsBackend Backend, Device Device) CreateDevice()
    {
        var backend = new FakeGraphicsBackend();
        return (backend, new Device(backend));
    }

    private static Texture2D RenderTarget(Device device)
        => new(device,
               new Texture2DDescription
                   {
                       Width = 16,
                       Height = 16,
                       Format = Format.R8G8B8A8_UNorm,
                       BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                       Usage = ResourceUsage.Default,
                   });

    private static Buffer ConstantBuffer(Device device)
        => new(device,
               new BufferDescription
                   {
                       SizeInBytes = 64,
                       BindFlags = BindFlags.ConstantBuffer,
                       Usage = ResourceUsage.Dynamic,
                       CpuAccessFlags = CpuAccessFlags.Write,
                   });

    private static SamplerStateDescription PointSampler
        => new()
               {
                   Filter = Filter.MinMagMipPoint,
                   AddressU = TextureAddressMode.Wrap,
                   AddressV = TextureAddressMode.Wrap,
                   AddressW = TextureAddressMode.Wrap,
                   MaximumLod = float.MaxValue,
               };

    private static int Slot(Binding[] bindings, BindingKind kind)
    {
        foreach (var binding in bindings)
        {
            if (binding.Kind == kind)
                return binding.Slot;
        }

        return -1;
    }
}

using System.Numerics;
using T3.Graphics;
using T3.Graphics.Compat;
using T3.Graphics.Vulkan;
using Xunit;
using Xunit.Abstractions;
using Buffer = T3.Graphics.Compat.Buffer;
using Format = T3.Graphics.Format;

namespace Core.Tests;

/// <summary>
/// Renders through the compatibility layer onto the Vulkan backend and reads the pixels back. This is the
/// only test that touches a GPU: it covers the path an image operator takes — upload a texture, bind it with
/// a sampler and a constant buffer, draw a full-screen triangle, copy the result to a staging texture and map
/// it — and it skips itself where no Vulkan device exists.
/// </summary>
/// <remarks>
/// The shaders are SPIR-V compiled from HLSL by Slang with the register shifts the backend expects
/// (s/b/t/u at 0/16/32/160, offset by <see cref="ShaderSlots.BaseOf"/> per stage). They are embedded so the
/// test needs no compiler.
/// </remarks>
public class VulkanBackendTests(ITestOutputHelper output)
{
    [Fact]
    public void ATexturedTriangleRendersAndReadsBack()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        output.WriteLine($"GPU: {backend.AdapterName}");

        var device = new Device(backend);
        var context = device.ImmediateContext;

        // A uniform source texture, so nothing depends on filtering or on where the triangle samples.
        var sourcePixels = new byte[Size * Size * 4];

        for (var i = 0; i < sourcePixels.Length; i += 4)
        {
            sourcePixels[i] = 200;
            sourcePixels[i + 1] = 100;
            sourcePixels[i + 2] = 50;
            sourcePixels[i + 3] = 255;
        }

        using var source = new Texture2D(device, Describe(BindFlags.ShaderResource, ResourceUsage.Default), sourcePixels);
        using var sourceView = new ShaderResourceView(device, source);

        using var target = new Texture2D(device, Describe(BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default));
        using var targetView = new RenderTargetView(device, target);

        // Half brightness, so a wrong constant buffer cannot pass as a correct one.
        var tint = new[] { 0.5f, 0.5f, 0.5f, 1f };
        using var constants = new Buffer(device,
                                         new BufferDescription
                                             {
                                                 SizeInBytes = 16,
                                                 BindFlags = BindFlags.ConstantBuffer,
                                                 Usage = ResourceUsage.Dynamic,
                                                 CpuAccessFlags = CpuAccessFlags.Write,
                                             },
                                         System.Runtime.InteropServices.MemoryMarshal.AsBytes<float>(tint));

        using var sampler = new SamplerState(device,
                                             new SamplerStateDescription
                                                 {
                                                     Filter = Filter.MinMagMipPoint,
                                                     AddressU = TextureAddressMode.Wrap,
                                                     AddressV = TextureAddressMode.Wrap,
                                                     AddressW = TextureAddressMode.Wrap,
                                                     MaximumLod = float.MaxValue,
                                                 });

        using var vertexShader = new T3.Graphics.Compat.VertexShader(device, backend.CreateShader(ShaderStage.Vertex, VertexSpirv, "main", [], "vs"));
        using var pixelShader = new T3.Graphics.Compat.PixelShader(device, backend.CreateShader(ShaderStage.Pixel, PixelSpirv, "main", PixelBindings,
                                                                                                "ps"));

        using var rasterizer = new RasterizerState(device,
                                                   new RasterizerStateDescription
                                                       {
                                                           FillMode = FillMode.Solid,
                                                           CullMode = CullMode.None,
                                                           IsDepthClipEnabled = true,
                                                       });

        using var depthStencil = new DepthStencilState(device, new DepthStencilStateDescription { IsDepthEnabled = false });

        device.BeginFrame();
        context.OutputMerger.SetTargets(targetView);
        context.OutputMerger.SetDepthStencilState(depthStencil);
        context.Rasterizer.State = rasterizer;
        context.Rasterizer.SetViewport(0, 0, Size, Size);
        context.ClearRenderTargetView(targetView, new Vector4(0, 0, 0, 1));
        context.VertexShader.Set(vertexShader);
        context.PixelShader.Set(pixelShader);
        context.PixelShader.SetShaderResource(0, sourceView);
        context.PixelShader.SetSampler(0, sampler);
        context.PixelShader.SetConstantBuffer(0, constants);
        context.Draw(3, 0);
        device.EndFrame();

        using var staging = new Texture2D(device, Describe(BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));

        device.BeginFrame();
        context.CopyResource(target, staging);
        var box = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        Assert.NotEqual(IntPtr.Zero, box.DataPointer);

        var pixels = new byte[Size * Size * 4];
        System.Runtime.InteropServices.Marshal.Copy(box.DataPointer, pixels, 0, pixels.Length);
        context.UnmapSubresource(staging, 0);

        output.WriteLine($"first pixel: {pixels[0]}, {pixels[1]}, {pixels[2]}, {pixels[3]}");

        // The source texture times the tint, within what 8-bit rounding allows.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            Assert.InRange(pixels[i], 95, 105);
            Assert.InRange(pixels[i + 1], 45, 55);
            Assert.InRange(pixels[i + 2], 20, 30);
            Assert.Equal(255, pixels[i + 3]);
        }

        AssertValidationStayedQuiet();
    }

    [Fact]
    public void AnUnboundSlotDoesNotCrashTheDraw()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var device = new Device(backend);
        var context = device.ImmediateContext;

        using var target = new Texture2D(device, Describe(BindFlags.RenderTarget, ResourceUsage.Default));
        using var targetView = new RenderTargetView(device, target);
        using var depthStencil = new DepthStencilState(device, new DepthStencilStateDescription { IsDepthEnabled = false });
        using var rasterizer = new RasterizerState(device, new RasterizerStateDescription { CullMode = CullMode.None, IsDepthClipEnabled = true });

        using var vertexShader = new T3.Graphics.Compat.VertexShader(device, backend.CreateShader(ShaderStage.Vertex, VertexSpirv, "main", [], "vs"));
        using var pixelShader = new T3.Graphics.Compat.PixelShader(device, backend.CreateShader(ShaderStage.Pixel, PixelSpirv, "main", PixelBindings,
                                                                                                "ps"));

        // The shader declares a texture, a sampler and a constant buffer, and this draw binds none of them.
        // Under D3D11 that read zeros instead of crashing, and operators depend on it.
        device.BeginFrame();
        context.OutputMerger.SetTargets(targetView);
        context.OutputMerger.SetDepthStencilState(depthStencil);
        context.Rasterizer.State = rasterizer;
        context.Rasterizer.SetViewport(0, 0, Size, Size);
        context.VertexShader.Set(vertexShader);
        context.PixelShader.Set(pixelShader);
        context.Draw(3, 0);
        device.EndFrame();

        using var staging = new Texture2D(device, Describe(BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));

        device.BeginFrame();
        context.CopyResource(target, staging);
        var box = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        context.UnmapSubresource(staging, 0);

        Assert.NotEqual(IntPtr.Zero, box.DataPointer);
        AssertValidationStayedQuiet();
    }

    [Fact]
    public void ThePipelineCacheReturnsTheSamePipelineForTheSameState()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        using var vertexShader = backend.CreateShader(ShaderStage.Vertex, VertexSpirv, "main", [], "vs");
        using var pixelShader = backend.CreateShader(ShaderStage.Pixel, PixelSpirv, "main", PixelBindings, "ps");

        var description = new GraphicsPipelineDescription
                              {
                                  VertexShader = vertexShader,
                                  PixelShader = pixelShader,
                                  Topology = Topology.TriangleList,
                                  RenderTargetCount = 1,
                                  Samples = new SampleDescription(1, 0),
                              };

        var formats = new FormatSet();
        formats[0] = Format.R8G8B8A8_UNorm;
        description = description with { RenderTargetFormats = formats };

        Assert.Same(backend.GetOrCreatePipeline(description), backend.GetOrCreatePipeline(description));

        // A blend state on a target the first pipeline did not blend has to produce a different pipeline —
        // the inline arrays in the description are why its equality is written out by hand.
        var blend = new BlendTargetStates();
        blend[0] = new BlendTargetState { Enabled = true, WriteMask = ColorComponents.All };
        var blended = description with { Blend = blend };

        Assert.NotSame(backend.GetOrCreatePipeline(description), backend.GetOrCreatePipeline(blended));
        AssertValidationStayedQuiet();
    }

    /// <summary>
    /// The validation layer reports through the backend's messenger, and the count is process-wide, so a
    /// test that pushes it up fails even if its own pixels looked right.
    /// </summary>
    private void AssertValidationStayedQuiet()
    {
        if (_validationErrorsAtStart < 0)
            return;

        Assert.Equal(_validationErrorsAtStart, VulkanBackend.ValidationErrorCount);
    }

    private static Texture2DDescription Describe(BindFlags bindFlags, ResourceUsage usage, CpuAccessFlags cpuAccess = CpuAccessFlags.None)
        => new()
               {
                   Width = Size,
                   Height = Size,
                   MipLevels = 1,
                   ArraySize = 1,
                   Format = Format.R8G8B8A8_UNorm,
                   SampleDescription = new SampleDescription(1, 0),
                   BindFlags = bindFlags,
                   Usage = usage,
                   CpuAccessFlags = cpuAccess,
               };

    /// <summary>Null when the machine has no usable Vulkan device, which is not a test failure.</summary>
    private VulkanBackend? TryCreateBackend()
    {
        try
        {
            GraphicsLog.Error = message => output.WriteLine(message);
            var backend = new VulkanBackend(enableValidation: true);
            _validationErrorsAtStart = VulkanBackend.ValidationErrorCount;
            return backend;
        }
        catch (Exception exception)
        {
            output.WriteLine($"Skipped: no Vulkan device ({exception.Message})");
            return null;
        }
    }

    private const int Size = 4;
    private int _validationErrorsAtStart = -1;

    /// <summary>What the pixel shader declares, as the shader compiler's reflection would report it.</summary>
    private static ShaderBinding[] PixelBindings =>
        [
            new(0, ShaderSlots.BaseOf(ShaderStage.Pixel) + ShaderSlots.SamplerBase, BindingKind.Sampler, "TexSampler"),
            new(0, ShaderSlots.BaseOf(ShaderStage.Pixel) + ShaderSlots.ConstantBufferBase, BindingKind.ConstantBuffer, "ParamConstants"),
            new(0, ShaderSlots.BaseOf(ShaderStage.Pixel) + ShaderSlots.ShaderResourceBase, BindingKind.SampledTexture, "InputTexture"),
        ];

    private static byte[] VertexSpirv { get; } = Convert.FromBase64String(
"AwIjBwAFAQAAACgAOQAAAAAAAAARAAIASxEAABEAAgABAAAADgADAAAAAAABAAAADwAJAAAAAAACAAAAbWFpbgAAAAA0AAAAOAAA"
            + "ABAAAAAOAAAAAwADAAsAAAABAAAABQAEAB8AAABxdWFkAAAAAAUACgA4AAAAZW50cnlQb2ludFBhcmFtX3ZzTWFpbi50ZXhDb29y"
            + "ZAAFAAQAAgAAAHZzTWFpbgAARwAEAA4AAAALAAAASBEAAEcABAAQAAAACwAAACoAAABHAAQANAAAAAsAAAAAAAAARwAEADgAAAAe"
            + "AAAAAAAAABMAAgABAAAAIQADAAMAAAABAAAAFgADAAYAAAAgAAAAFwAEAAcAAAAGAAAABAAAABcABAAIAAAABgAAAAIAAAAVAAQA"
            + "CwAAACAAAAABAAAAIAAEAA0AAAABAAAACwAAABUABAASAAAAIAAAAAAAAAArAAQACwAAABUAAAABAAAAKwAEABIAAAAXAAAAAgAA"
            + "ACsABAAGAAAAKAAAAAAAAEArAAQABgAAACkAAAAAAADALAAFAAgAAAAnAAAAKAAAACkAAAArAAQABgAAACwAAAAAAIC/KwAEAAYA"
            + "AAAtAAAAAACAPywABQAIAAAAKwAAACwAAAAtAAAAKwAEAAYAAAAvAAAAAAAAACAABAAzAAAAAwAAAAcAAAAgAAQANwAAAAMAAAAI"
            + "AAAAOwAEAA0AAAAOAAAAAQAAADsABAANAAAAEAAAAAEAAAA7AAQAMwAAADQAAAADAAAAOwAEADcAAAA4AAAAAwAAADYABQABAAAA"
            + "AgAAAAAAAAADAAAA+AACAAQAAAA9AAQACwAAAAwAAAAOAAAAPQAEAAsAAAAPAAAAEAAAAIIABQALAAAAEQAAAA8AAAAMAAAAfAAE"
            + "ABIAAAATAAAAEQAAAMQABQASAAAAFAAAABMAAAAVAAAAxwAFABIAAAAWAAAAFAAAABcAAABwAAQABgAAABgAAAAWAAAAPQAEAAsA"
            + "AAAZAAAADgAAAD0ABAALAAAAGgAAABAAAACCAAUACwAAABsAAAAaAAAAGQAAAHwABAASAAAAHAAAABsAAADHAAUAEgAAAB0AAAAc"
            + "AAAAFwAAAHAABAAGAAAAHgAAAB0AAABQAAUACAAAAB8AAAAYAAAAHgAAAIUABQAIAAAAJgAAAB8AAAAnAAAAgQAFAAgAAAAqAAAA"
            + "JgAAACsAAABQAAYABwAAAC4AAAAqAAAALwAAAC0AAAA+AAMANAAAAC4AAAA+AAMAOAAAAB8AAAD9AAEAOAABAA==");

    private static byte[] PixelSpirv { get; } = Convert.FromBase64String(
"AwIjBwAFAQAAACgAIgAAAAAAAAARAAIAAQAAAA4AAwAAAAAAAQAAAA8ACgAEAAAAAgAAAG1haW4AAAAADQAAABEAAAAZAAAAIQAA"
            + "AAkAAAAQAAMAAgAAAAcAAAADAAMACwAAAAEAAAAFAAYACQAAAGlucHV0LnRleENvb3JkAAAFAAYADQAAAElucHV0VGV4dHVyZQAA"
            + "AAAFAAUAEQAAAFRleFNhbXBsZXIAAAUABQAVAAAAX19zYW1wbGVkAAAABQANABcAAABTTEFOR19QYXJhbWV0ZXJHcm91cF9QYXJh"
            + "bUNvbnN0YW50c19kZWZhdWx0AAYABQAXAAAAAAAAAFRpbnQAAAAABQAGABkAAABQYXJhbUNvbnN0YW50cwAABQAIACEAAABlbnRy"
            + "eVBvaW50UGFyYW1fcHNNYWluAAAFAAQAAgAAAHBzTWFpbgAARwAEAAkAAAAeAAAAAAAAAEcABAANAAAAIQAAAOgAAABHAAQADQAA"
            + "ACIAAAAAAAAARwAEABEAAAAhAAAAyAAAAEcABAARAAAAIgAAAAAAAABHAAMAFwAAAAIAAABIAAUAFwAAAAAAAAAjAAAAAAAAAEcA"
            + "BAAZAAAAIQAAANgAAABHAAQAGQAAACIAAAAAAAAARwAEACEAAAAeAAAAAAAAABMAAgABAAAAIQADAAMAAAABAAAAFgADAAUAAAAg"
            + "AAAAFwAEAAYAAAAFAAAAAgAAACAABAAIAAAAAQAAAAYAAAAZAAkACgAAAAUAAAABAAAAAgAAAAAAAAAAAAAAAQAAAAAAAAAgAAQA"
            + "DAAAAAAAAAAKAAAAGgACAA4AAAAgAAQAEAAAAAAAAAAOAAAAGwADABIAAAAKAAAAFwAEABQAAAAFAAAABAAAAB4AAwAXAAAAFAAA"
            + "ACAABAAYAAAAAgAAABcAAAAVAAQAGgAAACAAAAABAAAAKwAEABoAAAAbAAAAAAAAACAABAAcAAAAAgAAABQAAAAgAAQAIAAAAAMA"
            + "AAAUAAAAOwAEAAgAAAAJAAAAAQAAADsABAAMAAAADQAAAAAAAAA7AAQAEAAAABEAAAAAAAAAOwAEABgAAAAZAAAAAgAAADsABAAg"
            + "AAAAIQAAAAMAAAA2AAUAAQAAAAIAAAAAAAAAAwAAAPgAAgAEAAAAPQAEAAYAAAAHAAAACQAAAD0ABAAKAAAACwAAAA0AAAA9AAQA"
            + "DgAAAA8AAAARAAAAVgAFABIAAAATAAAACwAAAA8AAABXAAYAFAAAABUAAAATAAAABwAAAAAAAABBAAUAHAAAAB0AAAAZAAAAGwAA"
            + "AD0ABAAUAAAAHgAAAB0AAACFAAUAFAAAAB8AAAAVAAAAHgAAAD4AAwAhAAAAHwAAAP0AAQA4AAEA");
}

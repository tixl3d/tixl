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
    /// How image loading builds a mipped texture: every level points at the one buffer that holds level 0,
    /// because the coarser levels are filtered down on the GPU straight after. A DataRectangle carries only a
    /// row pitch, so the compatibility layer has to work out how many bytes each level covers — taking the
    /// row pitch for it stages a single row and the copy then reads past the staging buffer, which faults the
    /// GPU rather than failing cleanly.
    /// </summary>
    [Fact]
    public void AMippedTextureUploadsEveryLevelFromOneBuffer()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var device = new Device(backend);

        const int width = 64;
        const int height = 64;
        var mipLevels = (int)Math.Log(width, 2.0) + 1;

        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)i;
        }

        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            var data = pinned.AddrOfPinnedObject();
            var stride = width * 4;
            var rectangles = new DataRectangle[mipLevels];

            for (var i = 0; i < mipLevels; i++)
            {
                rectangles[i] = new DataRectangle(data, stride);
                stride /= 2;
            }

            var description = Describe(BindFlags.ShaderResource, ResourceUsage.Default) with
                                  {
                                      Width = width,
                                      Height = height,
                                      MipLevels = mipLevels,
                                  };

            using var texture = new Texture2D(device, description, rectangles);
            Assert.Equal(mipLevels, texture.Description.MipLevels);
        }
        finally
        {
            pinned.Free();
        }

        AssertValidationStayedQuiet();
    }

    /// <summary>
    /// A multisampled render target, the way the RenderTarget operator asks for one. Vulkan only accepts a
    /// sample count the device reports for that format and usage, so a graph asking for more has to be given
    /// what the device has rather than an image that fails to create.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void AMultisampledRenderTargetIsCreatedAtASupportedSampleCount(int sampleCount)
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var device = new Device(backend);

        var description = new Texture2DDescription
                              {
                                  Width = 256,
                                  Height = 256,
                                  ArraySize = 1,
                                  MipLevels = 1,
                                  Format = Format.R16G16B16A16_Float,
                                  BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                                  Usage = ResourceUsage.Default,
                                  CpuAccessFlags = CpuAccessFlags.None,
                                  SampleDescription = new SampleDescription(sampleCount, 0),
                              };

        using var texture = new Texture2D(device, description);
        Assert.NotNull(texture.Native);

        // The views are where a texture that failed to create turns into a null reference far away.
        using var srv = new ShaderResourceView(device, texture);
        using var rtv = new RenderTargetView(device, texture,
                                             new RenderTargetViewDescription
                                                 {
                                                     Format = description.Format,
                                                     Dimension = sampleCount > 1
                                                                     ? RenderTargetViewDimension.Texture2DMultisampled
                                                                     : RenderTargetViewDimension.Texture2D,
                                                 });

        Assert.NotNull(srv.Native);
        Assert.NotNull(rtv.Native);
        output.WriteLine($"{sampleCount}x created with views");

        AssertValidationStayedQuiet();
    }

    /// <summary>
    /// Exactly what loading an image does: a non-square, non-power-of-two texture whose levels all alias the
    /// one buffer holding level 0, then mips generated on the GPU from it.
    /// </summary>
    [Fact]
    public void AnImageSizedTextureGeneratesItsMips()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var device = new Device(backend);
        var context = device.ImmediateContext;

        const int width = 640;
        const int height = 360;
        var mipLevels = (int)Math.Log(width, 2.0) + 1;
        output.WriteLine($"{width}x{height}, {mipLevels} mip levels");

        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)i;
        }

        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            var stride = width * 4;
            var rectangles = new DataRectangle[mipLevels];

            for (var i = 0; i < mipLevels; i++)
            {
                rectangles[i] = new DataRectangle(pinned.AddrOfPinnedObject(), stride);
                stride /= 2;
            }

            var description = new Texture2DDescription
                                  {
                                      Width = width,
                                      Height = height,
                                      ArraySize = 1,
                                      MipLevels = mipLevels,
                                      Format = Format.R8G8B8A8_UNorm,
                                      BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                                      Usage = ResourceUsage.Default,
                                      CpuAccessFlags = CpuAccessFlags.None,
                                      OptionFlags = ResourceOptionFlags.GenerateMipMaps,
                                      SampleDescription = new SampleDescription(1, 0),
                                  };

            using var texture = new Texture2D(device, description, rectangles);
            using var view = new ShaderResourceView(device, texture);

            device.BeginFrame();
            context.GenerateMips(view);
            device.EndFrame();
        }
        finally
        {
            pinned.Free();
        }

        AssertValidationStayedQuiet();
    }

    /// <summary>
    /// D3D11 ignores the pitches when the target is a buffer, so callers pass zero for both and expect the
    /// whole buffer to be written. Deriving the byte count from those pitches uploads nothing at all, which
    /// no validation layer complains about — the constants are simply never there.
    /// </summary>
    [Fact]
    public void AConstantBufferUpdatedWithoutPitchesReceivesItsData()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var device = new Device(backend);
        var context = device.ImmediateContext;

        var values = new float[] { 1, 2, 3, 4 };
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes<float>(values);

        using var constants = new Buffer(device,
                                         new BufferDescription
                                             {
                                                 SizeInBytes = bytes.Length,
                                                 BindFlags = BindFlags.ConstantBuffer,
                                                 Usage = ResourceUsage.Default,
                                             });

        using var staging = new Buffer(device,
                                       new BufferDescription
                                           {
                                               SizeInBytes = bytes.Length,
                                               BindFlags = BindFlags.None,
                                               Usage = ResourceUsage.Staging,
                                               CpuAccessFlags = CpuAccessFlags.Read,
                                           });

        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(values, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            device.BeginFrame();
            // Both pitches zero, the way PointLightStack and ResourceManager write their constants.
            context.UpdateSubresource(new DataBox(pinned.AddrOfPinnedObject(), 0, 0), constants);
            context.CopyResource(constants, staging);
            device.EndFrame();
        }
        finally
        {
            pinned.Free();
        }

        device.BeginFrame();
        var box = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        Assert.NotEqual(IntPtr.Zero, box.DataPointer);

        var readBack = new float[values.Length];
        System.Runtime.InteropServices.Marshal.Copy(box.DataPointer, readBack, 0, readBack.Length);
        context.UnmapSubresource(staging, 0);

        Assert.Equal(values, readBack);
        AssertValidationStayedQuiet();
    }

    /// <summary>
    /// A 2D upload that declares only a row pitch still covers every row of the subresource. Treating the
    /// zero slice pitch as the byte count stages one row, and the copy then reads past the staging buffer —
    /// the fault the camera and video operators would hit on their first frame.
    /// </summary>
    [Fact]
    public void A2DUploadWithoutASlicePitchCoversEveryRow()
    {
        using var backend = TryCreateBackend();

        if (backend == null)
            return;

        var device = new Device(backend);
        var context = device.ImmediateContext;

        var pixels = new byte[Size * Size * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i + 1);
        }

        using var texture = new Texture2D(device, Describe(BindFlags.ShaderResource, ResourceUsage.Default));
        using var staging = new Texture2D(device, Describe(BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));

        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            device.BeginFrame();
            // Row pitch only, the way the camera and video operators upload their frames.
            context.UpdateSubresource(new DataBox(pinned.AddrOfPinnedObject(), Size * 4, 0), texture);
            context.CopyResource(texture, staging);
            device.EndFrame();
        }
        finally
        {
            pinned.Free();
        }

        device.BeginFrame();
        var box = context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        Assert.NotEqual(IntPtr.Zero, box.DataPointer);

        var readBack = new byte[pixels.Length];
        System.Runtime.InteropServices.Marshal.Copy(box.DataPointer, readBack, 0, readBack.Length);
        context.UnmapSubresource(staging, 0);

        // The last row matters: staging only the first one leaves it at zero.
        Assert.Equal(pixels, readBack);
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

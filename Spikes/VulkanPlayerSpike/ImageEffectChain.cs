using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Vulkan;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// The image-effect chain TiXL builds from operators, here by hand: [MandelbrotFractal] renders into a render
/// target, and [Sharpen] samples that target into whatever the caller is drawing to. Both shaders are Lib's,
/// unmodified; their constants are written per frame into the uniform ring, the way [FloatsToBuffer] rewrites
/// its constant buffer every update.
/// </summary>
internal sealed class ImageEffectChain : IDisposable
{
    public ImageEffectChain(VulkanDevice device, string shaderRoot, VkFormat outputFormat, UniformRing uniforms,
                            string effectShader = "img/fx/Sharpen.hlsl", string effectEntry = "psMain")
    {
        _device = device;
        _uniforms = uniforms;
        _gradient = new GradientTexture(device);
        _samplers = new Samplers(device);

        // [RenderTarget] defaults to a half-float target; the final pass writes the swapchain's format.
        _generate = new FullscreenPass(device, shaderRoot, "img/generate/MandelbrotFractal.hlsl", "psMain", IntermediateFormat);
        _sharpen = new FullscreenPass(device, shaderRoot, effectShader, effectEntry, outputFormat);
    }

    public void EnsureSize(VkExtent2D extent)
    {
        if (_renderTarget != null && _renderTarget.Extent.width == extent.width && _renderTarget.Extent.height == extent.height)
            return;

        _device.DeviceApi.vkDeviceWaitIdle();
        _renderTarget?.Dispose();
        _renderTarget = new RenderTarget(_device, extent, IntermediateFormat);
    }

    /// <summary>Runs the generating pass into the off-screen target and leaves it ready to be sampled.</summary>
    public unsafe void RenderOffscreen(VkCommandBuffer commandBuffer, float timeSec)
    {
        var target = _renderTarget ?? throw new InvalidOperationException("EnsureSize first");
        var api = _device.DeviceApi;

        target.TransitionToColorAttachment(commandBuffer);

        VkRenderingAttachmentInfo colorAttachment = new()
                                                        {
                                                            imageView = target.View,
                                                            imageLayout = VkImageLayout.ColorAttachmentOptimal,
                                                            loadOp = VkAttachmentLoadOp.DontCare,
                                                            storeOp = VkAttachmentStoreOp.Store,
                                                        };
        VkRenderingInfo renderingInfo = new()
                                            {
                                                renderArea = new VkRect2D(VkOffset2D.Zero, target.Extent),
                                                layerCount = 1,
                                                colorAttachmentCount = 1,
                                                pColorAttachments = &colorAttachment,
                                            };
        api.vkCmdBeginRendering(commandBuffer, &renderingInfo);

        // Zooms into the seahorse valley and back while the palette cycles.
        _generate.SetConstantBuffer("ParamConstants", _uniforms.Write(new MandelbrotParams
                                                                         {
                                                                             Offset = new Vector2(-0.7436439f, 0.1318259f),
                                                                             Scale = 2.0f - 2.0f * MathF.Cos(timeSec * 0.25f),
                                                                             AspectRatio = (float)target.Extent.width / target.Extent.height,
                                                                             ColorScale = 48f,
                                                                             ColorPhase = timeSec * 0.1f,
                                                                         }));
        _generate.SetTexture("GradientImage", _gradient.View);
        _generate.SetSampler("texSampler", _samplers.Get(VkFilter.Linear, VkSamplerAddressMode.Repeat));
        _generate.Draw(commandBuffer, target.Extent);

        api.vkCmdEndRendering(commandBuffer);
        target.TransitionToShaderRead(commandBuffer);
    }

    /// <summary>Runs the effect pass into the caller's current attachment.</summary>
    public void DrawEffect(VkCommandBuffer commandBuffer, VkExtent2D extent, float timeSec)
    {
        var target = _renderTarget ?? throw new InvalidOperationException("EnsureSize first");

        _sharpen.SetConstantBuffer("ParamConstants", _uniforms.Write(new SharpenParams
                                                                        {
                                                                            SampleRadius = 1.5f + 1.5f * MathF.Sin(timeSec * 0.7f),
                                                                            Strength = EffectStrength,
                                                                            Clamping = 1f,
                                                                        }));
        _sharpen.SetTexture("Image", target.View);
        _sharpen.SetTexture("colorTexture", target.View); // name used by the pass-through shader
        var sampler = _samplers.Get(VkFilter.Linear, VkSamplerAddressMode.ClampToEdge);
        _sharpen.SetSampler("texSampler", sampler);
        _sharpen.SetSampler("samLinear", sampler);
        _sharpen.Draw(commandBuffer, extent);
    }

    public void Dispose()
    {
        _renderTarget?.Dispose();
        _sharpen.Dispose();
        _generate.Dispose();
        _samplers.Dispose();
        _gradient.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MandelbrotParams
    {
        public Vector2 Offset;
        public float Scale;
        public float AspectRatio;
        public float ColorScale;
        public float ColorPhase;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SharpenParams
    {
        public float SampleRadius;
        public float Strength;
        public float Clamping;
    }

    /// <summary>Sharpen amount; 0 must reproduce the source image exactly, which makes it a useful probe.</summary>
    public static float EffectStrength = 1.2f;

    private const VkFormat IntermediateFormat = VkFormat.R16G16B16A16Sfloat;

    private readonly VulkanDevice _device;
    private readonly UniformRing _uniforms;
    private readonly GradientTexture _gradient;
    private readonly Samplers _samplers;
    private readonly FullscreenPass _generate;
    private readonly FullscreenPass _sharpen;
    private RenderTarget? _renderTarget;
}

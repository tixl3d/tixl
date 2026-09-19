using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// Draws Lib's <c>MandelbrotFractal.hlsl</c> with the full-screen triangle from <c>dx11/fullscreen-texture.hlsl</c>,
/// exactly as the D3D11 image ops do. The descriptor layout comes from Slang's reflection, and resources are
/// bound by their HLSL names through push descriptors.
/// </summary>
internal sealed unsafe class MandelbrotPass : IDisposable
{
    public MandelbrotPass(VulkanDevice device, VkFormat colorFormat, string shaderRoot, int framesInFlight)
    {
        _device = device;
        var api = device.DeviceApi;

        var vertexShader = SlangShaderCompiler.Compile(shaderRoot, "dx11/fullscreen-texture.hlsl", "vsMain", ShaderStage.Vertex);
        var pixelShader = SlangShaderCompiler.Compile(shaderRoot, "img/generate/MandelbrotFractal.hlsl", "psMain", ShaderStage.Fragment);
        _bindings = MergeBindings(vertexShader.Bindings, pixelShader.Bindings);

        CreateUniformBuffer(framesInFlight);
        CreateGradientTexture();
        CreateSampler();

        var layoutBindings = stackalloc VkDescriptorSetLayoutBinding[_bindings.Count];
        for (var index = 0; index < _bindings.Count; index++)
        {
            var binding = _bindings[index];
            layoutBindings[index] = new VkDescriptorSetLayoutBinding
                                        {
                                            binding = binding.Binding,
                                            descriptorType = ToDescriptorType(binding),
                                            descriptorCount = 1,
                                            stageFlags = binding.Stages,
                                        };
        }

        VkDescriptorSetLayoutCreateInfo setLayoutInfo = new()
                                                            {
                                                                flags = VkDescriptorSetLayoutCreateFlags.PushDescriptor,
                                                                bindingCount = (uint)_bindings.Count,
                                                                pBindings = layoutBindings,
                                                            };
        api.vkCreateDescriptorSetLayout(&setLayoutInfo, null, out _descriptorSetLayout).CheckResult();

        var setLayout = _descriptorSetLayout;
        VkPipelineLayoutCreateInfo pipelineLayoutInfo = new()
                                                            {
                                                                setLayoutCount = 1,
                                                                pSetLayouts = &setLayout,
                                                            };
        api.vkCreatePipelineLayout(&pipelineLayoutInfo, null, out _pipelineLayout).CheckResult();

        _pipeline = CreatePipeline(vertexShader.Spirv, pixelShader.Spirv, colorFormat);
    }

    public void Draw(VkCommandBuffer commandBuffer, int frameIndex, VkExtent2D extent, float timeSec)
    {
        var api = _device.DeviceApi;

        // Zooms into the seahorse valley and back while the palette cycles.
        var frameSlice = _uniformMapped + frameIndex * UniformSlotSize * UniformSlotsPerFrame;
        *(ParamConstants*)frameSlice = new ParamConstants
                                           {
                                               Offset = new Vector2(-0.7436439f, 0.1318259f),
                                               Scale = 2.0f - 2.0f * MathF.Cos(timeSec * 0.25f),
                                               AspectRatio = (float)extent.width / extent.height,
                                               ColorScale = 48f,
                                               ColorPhase = timeSec * 0.1f,
                                           };
        *(ResolutionConstants*)(frameSlice + UniformSlotSize) = new ResolutionConstants
                                                                    {
                                                                        TargetWidth = extent.width,
                                                                        TargetHeight = extent.height,
                                                                    };

        api.vkCmdBindPipeline(commandBuffer, VkPipelineBindPoint.Graphics, _pipeline);

        // D3D's +Y-up clip space: a negative viewport height flips it without touching the shaders.
        VkViewport viewport = new()
                                  {
                                      x = 0,
                                      y = extent.height,
                                      width = extent.width,
                                      height = -(float)extent.height,
                                      minDepth = 0,
                                      maxDepth = 1,
                                  };
        api.vkCmdSetViewport(commandBuffer, 0, viewport);
        api.vkCmdSetScissor(commandBuffer, 0, new VkRect2D(VkOffset2D.Zero, extent));

        var writes = stackalloc VkWriteDescriptorSet[_bindings.Count];
        var bufferInfos = stackalloc VkDescriptorBufferInfo[_bindings.Count];
        var imageInfos = stackalloc VkDescriptorImageInfo[_bindings.Count];
        var frameOffset = (ulong)(frameIndex * UniformSlotSize * UniformSlotsPerFrame);
        for (var index = 0; index < _bindings.Count; index++)
        {
            var binding = _bindings[index];
            writes[index] = new VkWriteDescriptorSet
                                {
                                    dstBinding = binding.Binding,
                                    descriptorCount = 1,
                                    descriptorType = ToDescriptorType(binding),
                                };

            switch (binding.Name)
            {
                case "ParamConstants":
                    bufferInfos[index] = new VkDescriptorBufferInfo { buffer = _uniformBuffer, offset = frameOffset, range = UniformSlotSize };
                    writes[index].pBufferInfo = &bufferInfos[index];
                    break;
                case "Resolution":
                    bufferInfos[index] = new VkDescriptorBufferInfo { buffer = _uniformBuffer, offset = frameOffset + UniformSlotSize, range = UniformSlotSize };
                    writes[index].pBufferInfo = &bufferInfos[index];
                    break;
                case "GradientImage":
                    imageInfos[index] = new VkDescriptorImageInfo { imageView = _gradientView, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
                    writes[index].pImageInfo = &imageInfos[index];
                    break;
                case "texSampler":
                    imageInfos[index] = new VkDescriptorImageInfo { sampler = _sampler };
                    writes[index].pImageInfo = &imageInfos[index];
                    break;
                default:
                    throw new InvalidOperationException($"No resource for shader parameter '{binding.Name}'");
            }
        }

        api.vkCmdPushDescriptorSetKHR(commandBuffer, VkPipelineBindPoint.Graphics, _pipelineLayout, 0, (uint)_bindings.Count, writes);
        api.vkCmdDraw(commandBuffer, 3, 1, 0, 0);
    }

    public void Dispose()
    {
        var api = _device.DeviceApi;
        api.vkDeviceWaitIdle();
        api.vkDestroyPipeline(_pipeline);
        api.vkDestroyPipelineLayout(_pipelineLayout);
        api.vkDestroyDescriptorSetLayout(_descriptorSetLayout);
        api.vkDestroySampler(_sampler);
        api.vkDestroyImageView(_gradientView);
        api.vkDestroyImage(_gradientImage);
        api.vkFreeMemory(_gradientMemory);
        api.vkUnmapMemory(_uniformMemory);
        api.vkDestroyBuffer(_uniformBuffer);
        api.vkFreeMemory(_uniformMemory);
    }

    private VkPipeline CreatePipeline(byte[] vertexSpirv, byte[] pixelSpirv, VkFormat colorFormat)
    {
        var api = _device.DeviceApi;
        api.vkCreateShaderModule(vertexSpirv, null, out var vertexModule).CheckResult();
        api.vkCreateShaderModule(pixelSpirv, null, out var pixelModule).CheckResult();

        VkUtf8ReadOnlyString entryPoint = "main"u8; // Slang names every SPIR-V entry point "main"
        var stages = stackalloc VkPipelineShaderStageCreateInfo[2];
        stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = vertexModule, pName = entryPoint };
        stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = pixelModule, pName = entryPoint };

        VkPipelineVertexInputStateCreateInfo vertexInputState = new(); // vertices come from SV_VertexID
        VkPipelineInputAssemblyStateCreateInfo inputAssemblyState = new(VkPrimitiveTopology.TriangleList);
        VkPipelineViewportStateCreateInfo viewportState = new(1, 1);
        VkPipelineRasterizationStateCreateInfo rasterizationState = new()
                                                                        {
                                                                            polygonMode = VkPolygonMode.Fill,
                                                                            cullMode = VkCullModeFlags.None,
                                                                            frontFace = VkFrontFace.Clockwise,
                                                                            lineWidth = 1,
                                                                        };
        var multisampleState = VkPipelineMultisampleStateCreateInfo.Default;
        VkPipelineColorBlendAttachmentState blendAttachment = new() { colorWriteMask = VkColorComponentFlags.All };
        VkPipelineColorBlendStateCreateInfo colorBlendState = new(blendAttachment);

        var dynamicStates = stackalloc VkDynamicState[] { VkDynamicState.Viewport, VkDynamicState.Scissor };
        VkPipelineDynamicStateCreateInfo dynamicState = new() { dynamicStateCount = 2, pDynamicStates = dynamicStates };

        VkPipelineRenderingCreateInfo renderingInfo = new() { colorAttachmentCount = 1, pColorAttachmentFormats = &colorFormat };

        VkGraphicsPipelineCreateInfo pipelineInfo = new()
                                                        {
                                                            pNext = &renderingInfo,
                                                            stageCount = 2,
                                                            pStages = stages,
                                                            pVertexInputState = &vertexInputState,
                                                            pInputAssemblyState = &inputAssemblyState,
                                                            pViewportState = &viewportState,
                                                            pRasterizationState = &rasterizationState,
                                                            pMultisampleState = &multisampleState,
                                                            pColorBlendState = &colorBlendState,
                                                            pDynamicState = &dynamicState,
                                                            layout = _pipelineLayout,
                                                        };
        api.vkCreateGraphicsPipeline(pipelineInfo, out var pipeline).CheckResult();

        api.vkDestroyShaderModule(vertexModule);
        api.vkDestroyShaderModule(pixelModule);
        return pipeline;
    }

    private void CreateUniformBuffer(int framesInFlight)
    {
        var api = _device.DeviceApi;
        var size = (ulong)(framesInFlight * UniformSlotsPerFrame * UniformSlotSize);
        VkBufferCreateInfo bufferInfo = new() { size = size, usage = VkBufferUsageFlags.UniformBuffer };
        api.vkCreateBuffer(&bufferInfo, null, out _uniformBuffer).CheckResult();

        _uniformMemory = AllocateMemory(_uniformBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        api.vkBindBufferMemory(_uniformBuffer, _uniformMemory, 0).CheckResult();

        void* mapped;
        api.vkMapMemory(_uniformMemory, 0, size, 0, &mapped).CheckResult();
        _uniformMapped = (byte*)mapped;
    }

    /// <summary>A periodic cosine palette, so sampling with wrap addressing cycles smoothly.</summary>
    private void CreateGradientTexture()
    {
        var api = _device.DeviceApi;
        const int width = 256;

        VkBufferCreateInfo stagingInfo = new() { size = width * 4, usage = VkBufferUsageFlags.TransferSrc };
        api.vkCreateBuffer(&stagingInfo, null, out var stagingBuffer).CheckResult();
        var stagingMemory = AllocateMemory(stagingBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        api.vkBindBufferMemory(stagingBuffer, stagingMemory, 0).CheckResult();

        void* mapped;
        api.vkMapMemory(stagingMemory, 0, width * 4, 0, &mapped).CheckResult();
        var pixels = (byte*)mapped;
        for (var x = 0; x < width; x++)
        {
            var t = x / (float)width;
            pixels[x * 4 + 0] = PaletteChannel(t, 0.00f);
            pixels[x * 4 + 1] = PaletteChannel(t, 0.10f);
            pixels[x * 4 + 2] = PaletteChannel(t, 0.20f);
            pixels[x * 4 + 3] = 255;
        }

        api.vkUnmapMemory(stagingMemory);

        VkImageCreateInfo imageInfo = new()
                                          {
                                              imageType = VkImageType.Image2D,
                                              format = VkFormat.R8G8B8A8Unorm,
                                              extent = new VkExtent3D(width, 1, 1),
                                              mipLevels = 1,
                                              arrayLayers = 1,
                                              samples = VkSampleCountFlags.Count1,
                                              tiling = VkImageTiling.Optimal,
                                              usage = VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst,
                                              initialLayout = VkImageLayout.Undefined,
                                          };
        api.vkCreateImage(&imageInfo, null, out _gradientImage).CheckResult();
        api.vkGetImageMemoryRequirements(_gradientImage, out var requirements);
        VkMemoryAllocateInfo allocateInfo = new()
                                                {
                                                    allocationSize = requirements.size,
                                                    memoryTypeIndex = _device.FindMemoryType(requirements.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal),
                                                };
        api.vkAllocateMemory(&allocateInfo, null, out _gradientMemory).CheckResult();
        api.vkBindImageMemory(_gradientImage, _gradientMemory, 0).CheckResult();

        var image = _gradientImage;
        _device.SubmitAndWait(commandBuffer =>
                              {
                                  _device.TransitionImage(commandBuffer, image,
                                                          VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal,
                                                          VkPipelineStageFlags2.None, VkAccessFlags2.None,
                                                          VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);
                                  VkBufferImageCopy region = new()
                                                                 {
                                                                     imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                                                                     imageExtent = new VkExtent3D(width, 1, 1),
                                                                 };
                                  _device.DeviceApi.vkCmdCopyBufferToImage(commandBuffer, stagingBuffer, image,
                                                                           VkImageLayout.TransferDstOptimal, 1, &region);
                                  _device.TransitionImage(commandBuffer, image,
                                                          VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
                                                          VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite,
                                                          VkPipelineStageFlags2.FragmentShader, VkAccessFlags2.ShaderSampledRead);
                              });

        api.vkDestroyBuffer(stagingBuffer);
        api.vkFreeMemory(stagingMemory);

        VkImageViewCreateInfo viewInfo = new(_gradientImage,
                                             VkImageViewType.Image2D,
                                             VkFormat.R8G8B8A8Unorm,
                                             VkComponentMapping.Rgba,
                                             new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1));
        api.vkCreateImageView(&viewInfo, null, out _gradientView).CheckResult();
    }

    private void CreateSampler()
    {
        VkSamplerCreateInfo samplerInfo = new()
                                              {
                                                  magFilter = VkFilter.Linear,
                                                  minFilter = VkFilter.Linear,
                                                  mipmapMode = VkSamplerMipmapMode.Linear,
                                                  addressModeU = VkSamplerAddressMode.Repeat,
                                                  addressModeV = VkSamplerAddressMode.ClampToEdge,
                                                  addressModeW = VkSamplerAddressMode.ClampToEdge,
                                                  maxLod = 1000,
                                              };
        _device.DeviceApi.vkCreateSampler(&samplerInfo, null, out _sampler).CheckResult();
    }

    private VkDeviceMemory AllocateMemory(VkBuffer buffer, VkMemoryPropertyFlags properties)
    {
        _device.DeviceApi.vkGetBufferMemoryRequirements(buffer, out var requirements);
        VkMemoryAllocateInfo allocateInfo = new()
                                                {
                                                    allocationSize = requirements.size,
                                                    memoryTypeIndex = _device.FindMemoryType(requirements.memoryTypeBits, properties),
                                                };
        _device.DeviceApi.vkAllocateMemory(&allocateInfo, null, out var memory).CheckResult();
        return memory;
    }

    private static byte PaletteChannel(float t, float phase)
    {
        var value = 0.5f + 0.5f * MathF.Cos(MathF.Tau * (t + phase));
        return (byte)(value * 255);
    }

    private static List<MergedBinding> MergeBindings(List<ShaderBinding> vertexBindings, List<ShaderBinding> pixelBindings)
    {
        var merged = new Dictionary<uint, MergedBinding>();
        foreach (var binding in vertexBindings.Concat(pixelBindings))
        {
            var stageFlag = binding.Stage == ShaderStage.Vertex ? VkShaderStageFlags.Vertex : VkShaderStageFlags.Fragment;
            if (merged.TryGetValue(binding.Binding, out var existing))
            {
                if (existing.Name != binding.Name || existing.Kind != binding.Kind)
                    throw new InvalidOperationException($"Binding {binding.Binding} is '{existing.Name}' in one stage and '{binding.Name}' in another");

                merged[binding.Binding] = existing with { Stages = existing.Stages | stageFlag };
            }
            else
            {
                merged[binding.Binding] = new MergedBinding(binding.Name, binding.Kind, binding.Binding, stageFlag);
            }
        }

        return merged.Values.OrderBy(binding => binding.Binding).ToList();
    }

    private static VkDescriptorType ToDescriptorType(MergedBinding binding)
    {
        return binding.Kind switch
                   {
                       ShaderBindingKind.ConstantBuffer => VkDescriptorType.UniformBuffer,
                       ShaderBindingKind.ShaderResource => VkDescriptorType.SampledImage,
                       ShaderBindingKind.Sampler        => VkDescriptorType.Sampler,
                       _                                => throw new NotSupportedException($"'{binding.Name}': {binding.Kind} is not supported by the spike"),
                   };
    }

    private sealed record MergedBinding(string Name, ShaderBindingKind Kind, uint Binding, VkShaderStageFlags Stages);

    [StructLayout(LayoutKind.Sequential)]
    private struct ParamConstants
    {
        public Vector2 Offset;
        public float Scale;
        public float AspectRatio;
        public float ColorScale;
        public float ColorPhase;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ResolutionConstants
    {
        public float TargetWidth;
        public float TargetHeight;
    }

    /** Covers minUniformBufferOffsetAlignment on every desktop GPU. */
    private const int UniformSlotSize = 256;
    private const int UniformSlotsPerFrame = 2;

    private readonly VulkanDevice _device;
    private readonly List<MergedBinding> _bindings;
    private readonly VkDescriptorSetLayout _descriptorSetLayout;
    private readonly VkPipelineLayout _pipelineLayout;
    private readonly VkPipeline _pipeline;
    private VkBuffer _uniformBuffer;
    private VkDeviceMemory _uniformMemory;
    private byte* _uniformMapped;
    private VkImage _gradientImage;
    private VkDeviceMemory _gradientMemory;
    private VkImageView _gradientView;
    private VkSampler _sampler;
}

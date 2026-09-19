using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// One full-screen shader pass, the shape of TiXL's image-effect operators: the full-screen triangle from
/// <c>dx11/fullscreen-texture.hlsl</c>, a pixel shader from Lib, and resources bound by the names its HLSL
/// declares. Slang's reflection decides the descriptor layout, so any image shader works without a hand-written
/// binding table.
/// </summary>
internal sealed unsafe class FullscreenPass : IDisposable
{
    public FullscreenPass(VulkanDevice device, string shaderRoot, string pixelShaderPath, string entryPoint,
                          VkFormat colorFormat, bool alphaBlend = false)
    {
        _device = device;
        Name = $"{pixelShaderPath}:{entryPoint}";

        var vertexShader = SlangShaderCompiler.Compile(shaderRoot, "dx11/fullscreen-texture.hlsl", "vsMain", ShaderStage.Vertex);
        var pixelShader = SlangShaderCompiler.Compile(shaderRoot, pixelShaderPath, entryPoint, ShaderStage.Fragment);

        // Debug aid: run a module from another compiler (glslang) with Slang's reflection, to tell a Slang
        // codegen problem apart from a driver one.
        var spirvOverride = Environment.GetEnvironmentVariable("TIXL_EFFECT_SPIRV");
        if (spirvOverride != null && !pixelShaderPath.Contains("fullscreen-texture"))
            pixelShader = pixelShader with { Spirv = File.ReadAllBytes(spirvOverride) };
        _bindings = MergeBindings(vertexShader.Bindings, pixelShader.Bindings);
        _bufferInfos = new VkDescriptorBufferInfo[_bindings.Count];
        _imageInfos = new VkDescriptorImageInfo[_bindings.Count];

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

        // Debug aid: TIXL_NO_PUSH=1 uses a conventional descriptor set instead of push descriptors.
        _usePushDescriptors = Environment.GetEnvironmentVariable("TIXL_NO_PUSH") != "1";
        VkDescriptorSetLayoutCreateInfo setLayoutInfo = new()
                                                            {
                                                                flags = _usePushDescriptors
                                                                            ? VkDescriptorSetLayoutCreateFlags.PushDescriptor
                                                                            : VkDescriptorSetLayoutCreateFlags.None,
                                                                bindingCount = (uint)_bindings.Count,
                                                                pBindings = layoutBindings,
                                                            };
        device.DeviceApi.vkCreateDescriptorSetLayout(&setLayoutInfo, null, out _descriptorSetLayout).CheckResult();

        var setLayout = _descriptorSetLayout;
        VkPipelineLayoutCreateInfo pipelineLayoutInfo = new() { setLayoutCount = 1, pSetLayouts = &setLayout };
        device.DeviceApi.vkCreatePipelineLayout(&pipelineLayoutInfo, null, out _pipelineLayout).CheckResult();

        _pipeline = CreatePipeline(vertexShader.Spirv, pixelShader.Spirv, colorFormat, alphaBlend);

        if (!_usePushDescriptors)
            CreateDescriptorSet();
    }

    private void CreateDescriptorSet()
    {
        var api = _device.DeviceApi;
        var poolSizes = stackalloc VkDescriptorPoolSize[3];
        poolSizes[0] = new VkDescriptorPoolSize(VkDescriptorType.UniformBuffer, 8);
        poolSizes[1] = new VkDescriptorPoolSize(VkDescriptorType.SampledImage, 8);
        poolSizes[2] = new VkDescriptorPoolSize(VkDescriptorType.Sampler, 8);
        VkDescriptorPoolCreateInfo poolInfo = new() { maxSets = 1, poolSizeCount = 3, pPoolSizes = poolSizes };
        api.vkCreateDescriptorPool(&poolInfo, null, out _descriptorPool).CheckResult();

        var layout = _descriptorSetLayout;
        VkDescriptorSetAllocateInfo allocateInfo = new()
                                                       {
                                                           descriptorPool = _descriptorPool,
                                                           descriptorSetCount = 1,
                                                           pSetLayouts = &layout,
                                                       };
        VkDescriptorSet set;
        api.vkAllocateDescriptorSets(&allocateInfo, &set).CheckResult();
        _descriptorSet = set;
    }

    public string Name { get; }

    /// <summary>Binds a constant buffer slice to the cbuffer of that name, e.g. "ParamConstants".</summary>
    public void SetConstantBuffer(string name, (VkBuffer Buffer, ulong Offset, ulong Range) slice)
    {
        var index = IndexOf(name);
        if (index < 0)
            return; // the shader does not use it — Slang drops unused cbuffers

        _bufferInfos[index] = new VkDescriptorBufferInfo { buffer = slice.Buffer, offset = slice.Offset, range = slice.Range };
    }

    public void SetTexture(string name, VkImageView view)
    {
        var index = IndexOf(name);
        if (index >= 0)
            _imageInfos[index] = new VkDescriptorImageInfo { imageView = view, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
    }

    public void SetSampler(string name, VkSampler sampler)
    {
        var index = IndexOf(name);
        if (index >= 0)
            _imageInfos[index] = new VkDescriptorImageInfo { sampler = sampler };
    }

    public void Draw(VkCommandBuffer commandBuffer, VkExtent2D extent)
    {
        var api = _device.DeviceApi;
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
        fixed (VkDescriptorBufferInfo* bufferInfos = _bufferInfos)
        fixed (VkDescriptorImageInfo* imageInfos = _imageInfos)
        {
            for (var index = 0; index < _bindings.Count; index++)
            {
                var binding = _bindings[index];
                var descriptorType = ToDescriptorType(binding);
                writes[index] = new VkWriteDescriptorSet
                                    {
                                        dstBinding = binding.Binding,
                                        descriptorCount = 1,
                                        descriptorType = descriptorType,
                                    };
                if (descriptorType == VkDescriptorType.UniformBuffer)
                {
                    if (bufferInfos[index].buffer.IsNull)
                        throw new InvalidOperationException($"{Name}: nothing bound to '{binding.Name}'");

                    writes[index].pBufferInfo = &bufferInfos[index];
                }
                else
                {
                    writes[index].pImageInfo = &imageInfos[index];
                }
            }

            if (_usePushDescriptors)
            {
                api.vkCmdPushDescriptorSetKHR(commandBuffer, VkPipelineBindPoint.Graphics, _pipelineLayout, 0,
                                              (uint)_bindings.Count, writes);
            }
            else
            {
                for (var index = 0; index < _bindings.Count; index++)
                {
                    writes[index].dstSet = _descriptorSet;
                }

                api.vkUpdateDescriptorSets((uint)_bindings.Count, writes, 0, null);
                var set = _descriptorSet;
                api.vkCmdBindDescriptorSets(commandBuffer, VkPipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &set, 0, null);
            }
        }

        api.vkCmdDraw(commandBuffer, 3, 1, 0, 0);
    }

    public void Dispose()
    {
        var api = _device.DeviceApi;
        api.vkDestroyPipeline(_pipeline);
        api.vkDestroyPipelineLayout(_pipelineLayout);
        api.vkDestroyDescriptorSetLayout(_descriptorSetLayout);
        if (!_descriptorPool.IsNull)
            api.vkDestroyDescriptorPool(_descriptorPool);
    }

    private int IndexOf(string name)
    {
        for (var index = 0; index < _bindings.Count; index++)
        {
            if (_bindings[index].Name == name)
                return index;
        }

        return -1;
    }

    private VkPipeline CreatePipeline(byte[] vertexSpirv, byte[] pixelSpirv, VkFormat colorFormat, bool alphaBlend)
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

        // Mirrors [PickBlendMode]'s two most used modes.
        VkPipelineColorBlendAttachmentState blendAttachment = new()
                                                                  {
                                                                      colorWriteMask = VkColorComponentFlags.All,
                                                                      blendEnable = alphaBlend,
                                                                      srcColorBlendFactor = VkBlendFactor.SrcAlpha,
                                                                      dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                                                                      colorBlendOp = VkBlendOp.Add,
                                                                      srcAlphaBlendFactor = VkBlendFactor.One,
                                                                      dstAlphaBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                                                                      alphaBlendOp = VkBlendOp.Add,
                                                                  };
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

    private readonly VulkanDevice _device;
    private readonly List<MergedBinding> _bindings;
    private readonly VkDescriptorBufferInfo[] _bufferInfos;
    private readonly VkDescriptorImageInfo[] _imageInfos;
    private readonly VkDescriptorSetLayout _descriptorSetLayout;
    private readonly VkPipelineLayout _pipelineLayout;
    private readonly VkPipeline _pipeline;
    private readonly bool _usePushDescriptors;
    private VkDescriptorPool _descriptorPool;
    private VkDescriptorSet _descriptorSet;
}

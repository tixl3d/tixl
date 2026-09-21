using T3.Graphics;
using Vortice.Vulkan;

namespace T3.Graphics.Vulkan;

/// <summary>
/// Builds a Vulkan pipeline from one of the backend API's descriptions. The descriptor set layouts come from
/// what the shaders declare, because Vulkan needs the full layout before a pipeline exists — a draw that
/// leaves a slot unbound must still match a layout that names it.
/// </summary>
internal static unsafe class VulkanPipelineFactory
{
    internal static VulkanPipeline CreateGraphics(VulkanBackend backend, in GraphicsPipelineDescription description)
    {
        var api = backend.Api;
        var shaders = new List<VulkanShader>(3);

        if (description.VertexShader is VulkanShader vertex)
            shaders.Add(vertex);

        if (description.PixelShader is VulkanShader pixel)
            shaders.Add(pixel);

        if (description.GeometryShader is VulkanShader geometry)
            shaders.Add(geometry);

        var layout = CreateLayout(backend, shaders, out var setLayouts, out var declared);

        VkUtf8ReadOnlyString entryPoint = "main"u8; // Slang names every SPIR-V entry point main
        var stages = stackalloc VkPipelineShaderStageCreateInfo[shaders.Count];

        for (var i = 0; i < shaders.Count; i++)
        {
            stages[i] = new VkPipelineShaderStageCreateInfo
                            {
                                stage = VulkanConvert.ToVulkan(shaders[i].Stage),
                                module = shaders[i].Module,
                                pName = entryPoint,
                            };
        }

        var attributes = description.VertexLayout ?? [];
        var bindingDescriptions = stackalloc VkVertexInputBindingDescription[BlendTargetStates.MaxRenderTargets];
        var attributeDescriptions = stackalloc VkVertexInputAttributeDescription[Math.Max(1, attributes.Length)];
        var bindingCount = 0;

        for (var i = 0; i < attributes.Length; i++)
        {
            var attribute = attributes[i];

            attributeDescriptions[i] = new VkVertexInputAttributeDescription
                                           {
                                               location = (uint)i,
                                               binding = (uint)attribute.Buffer,
                                               format = VulkanConvert.ToVulkan(attribute.Format),
                                               offset = (uint)attribute.Offset,
                                           };

            if (attribute.Buffer >= bindingCount)
                bindingCount = attribute.Buffer + 1;
        }

        for (var i = 0; i < bindingCount; i++)
        {
            var stride = 0;
            var rate = VkVertexInputRate.Vertex;

            foreach (var attribute in attributes)
            {
                if (attribute.Buffer != i)
                    continue;

                stride = Math.Max(stride, attribute.Offset + FormatSizes.BytesPerPixel(attribute.Format));

                if (attribute.Rate == VertexInputRate.PerInstance)
                    rate = VkVertexInputRate.Instance;
            }

            bindingDescriptions[i] = new VkVertexInputBindingDescription { binding = (uint)i, stride = (uint)stride, inputRate = rate };
        }

        VkPipelineVertexInputStateCreateInfo vertexInput = new()
                                                               {
                                                                   vertexBindingDescriptionCount = (uint)bindingCount,
                                                                   pVertexBindingDescriptions = bindingCount > 0 ? bindingDescriptions : null,
                                                                   vertexAttributeDescriptionCount = (uint)attributes.Length,
                                                                   pVertexAttributeDescriptions = attributes.Length > 0
                                                                                                      ? attributeDescriptions
                                                                                                      : null,
                                                               };

        VkPipelineInputAssemblyStateCreateInfo inputAssembly = new()
                                                                  {
                                                                      topology = VulkanConvert.ToVulkan(description.Topology),
                                                                      primitiveRestartEnable = false,
                                                                  };

        VkPipelineTessellationStateCreateInfo tessellation = new() { patchControlPoints = (uint)Math.Max(0, description.PatchControlPoints) };
        VkPipelineViewportStateCreateInfo viewport = new() { viewportCount = 1, scissorCount = 1 };

        VkPipelineRasterizationStateCreateInfo rasterization = new()
                                                                  {
                                                                      depthClampEnable = !description.Rasterizer.DepthClip,
                                                                      polygonMode = description.Rasterizer.Fill == PolygonMode.Wireframe
                                                                                        ? VkPolygonMode.Line
                                                                                        : VkPolygonMode.Fill,
                                                                      cullMode = description.Rasterizer.Cull switch
                                                                                     {
                                                                                         FaceCulling.Front => VkCullModeFlags.Front,
                                                                                         FaceCulling.Back  => VkCullModeFlags.Back,
                                                                                         _                 => VkCullModeFlags.None,
                                                                                     },

                                                                      // D3D's front face is clockwise unless an
                                                                      // operator says otherwise, and the viewport is
                                                                      // flipped rather than the winding. D3D's own
                                                                      // viewport transform flips Y from clip space to
                                                                      // screen, and the negative height reproduces that
                                                                      // flip, so winding on screen matches D3D exactly.
                                                                      frontFace = description.Rasterizer.FrontFaceIsCounterClockwise
                                                                                      ? VkFrontFace.CounterClockwise
                                                                                      : VkFrontFace.Clockwise,
                                                                      depthBiasEnable = description.Rasterizer.DepthBias != 0
                                                                                        || description.Rasterizer.SlopeScaledDepthBias != 0,
                                                                      depthBiasConstantFactor = description.Rasterizer.DepthBias,
                                                                      depthBiasClamp = description.Rasterizer.DepthBiasClamp,
                                                                      depthBiasSlopeFactor = description.Rasterizer.SlopeScaledDepthBias,
                                                                      lineWidth = 1f,
                                                                  };

        VkPipelineMultisampleStateCreateInfo multisample = new()
                                                              {
                                                                  rasterizationSamples = description.Samples.Count >= 2
                                                                                             ? (VkSampleCountFlags)description.Samples.Count
                                                                                             : VkSampleCountFlags.Count1,
                                                                  alphaToCoverageEnable = description.AlphaToCoverage,
                                                                  minSampleShading = 1f,
                                                              };

        var stencilFront = ToStencil(description.DepthStencil.Front, description.DepthStencil.StencilReadMask,
                                     description.DepthStencil.StencilWriteMask);
        var stencilBack = ToStencil(description.DepthStencil.Back, description.DepthStencil.StencilReadMask,
                                    description.DepthStencil.StencilWriteMask);

        VkPipelineDepthStencilStateCreateInfo depthStencil = new()
                                                                {
                                                                    depthTestEnable = description.DepthStencil.DepthTest,
                                                                    depthWriteEnable = description.DepthStencil.DepthWrite,
                                                                    depthCompareOp = VulkanConvert.ToVulkan(description.DepthStencil.DepthCompare),
                                                                    stencilTestEnable = description.DepthStencil.StencilTest,
                                                                    front = stencilFront,
                                                                    back = stencilBack,
                                                                    maxDepthBounds = 1f,
                                                                };

        var attachmentCount = Math.Max(1, description.RenderTargetCount);
        var blendAttachments = stackalloc VkPipelineColorBlendAttachmentState[attachmentCount];
        var colorFormats = stackalloc VkFormat[attachmentCount];

        for (var i = 0; i < attachmentCount; i++)
        {
            var blend = description.Blend[i];

            blendAttachments[i] = new VkPipelineColorBlendAttachmentState
                                      {
                                          blendEnable = blend.Enabled,
                                          srcColorBlendFactor = VulkanConvert.ToVulkan(blend.SourceColor),
                                          dstColorBlendFactor = VulkanConvert.ToVulkan(blend.DestinationColor),
                                          colorBlendOp = VulkanConvert.ToVulkan(blend.ColorOp),
                                          srcAlphaBlendFactor = VulkanConvert.ToVulkan(blend.SourceAlpha),
                                          dstAlphaBlendFactor = VulkanConvert.ToVulkan(blend.DestinationAlpha),
                                          alphaBlendOp = VulkanConvert.ToVulkan(blend.AlphaOp),
                                          colorWriteMask = VulkanConvert.ToVulkan(blend.WriteMask),
                                      };

            colorFormats[i] = VulkanConvert.ToVulkan(description.RenderTargetFormats[i]);
        }

        VkPipelineColorBlendStateCreateInfo colorBlend = new()
                                                            {
                                                                attachmentCount = (uint)description.RenderTargetCount,
                                                                pAttachments = description.RenderTargetCount > 0 ? blendAttachments : null,
                                                            };

        var dynamicStates = stackalloc VkDynamicState[]
                                {
                                    VkDynamicState.Viewport,
                                    VkDynamicState.Scissor,
                                    VkDynamicState.BlendConstants,
                                    VkDynamicState.StencilReference,
                                };

        VkPipelineDynamicStateCreateInfo dynamic = new() { dynamicStateCount = 4, pDynamicStates = dynamicStates };

        var depthFormat = VulkanConvert.ToVulkan(description.DepthStencilFormat);

        VkPipelineRenderingCreateInfo rendering = new()
                                                     {
                                                         colorAttachmentCount = (uint)description.RenderTargetCount,
                                                         pColorAttachmentFormats = description.RenderTargetCount > 0 ? colorFormats : null,
                                                         depthAttachmentFormat = depthFormat,
                                                         stencilAttachmentFormat = VulkanConvert.HasStencil(description.DepthStencilFormat)
                                                                                       ? depthFormat
                                                                                       : VkFormat.Undefined,
                                                     };

        VkGraphicsPipelineCreateInfo pipelineInfo = new()
                                                        {
                                                            pNext = &rendering,
                                                            stageCount = (uint)shaders.Count,
                                                            pStages = stages,
                                                            pVertexInputState = &vertexInput,
                                                            pInputAssemblyState = &inputAssembly,
                                                            pTessellationState = &tessellation,
                                                            pViewportState = &viewport,
                                                            pRasterizationState = &rasterization,
                                                            pMultisampleState = &multisample,
                                                            pDepthStencilState = &depthStencil,
                                                            pColorBlendState = &colorBlend,
                                                            pDynamicState = &dynamic,
                                                            layout = layout,
                                                        };

        api.vkCreateGraphicsPipeline(pipelineInfo, out var pipeline).CheckResult();
        return new VulkanPipeline(backend, pipeline, layout, setLayouts, VkPipelineBindPoint.Graphics, declared, null);
    }

    internal static VulkanPipeline CreateCompute(VulkanBackend backend, in ComputePipelineDescription description)
    {
        var shaders = new List<VulkanShader>(1);

        if (description.ComputeShader is VulkanShader compute)
            shaders.Add(compute);

        var layout = CreateLayout(backend, shaders, out var setLayouts, out var declared);

        VkUtf8ReadOnlyString entryPoint = "main"u8;

        VkComputePipelineCreateInfo pipelineInfo = new()
                                                       {
                                                           stage = new VkPipelineShaderStageCreateInfo
                                                                       {
                                                                           stage = VkShaderStageFlags.Compute,
                                                                           module = shaders.Count > 0 ? shaders[0].Module : VkShaderModule.Null,
                                                                           pName = entryPoint,
                                                                       },
                                                           layout = layout,
                                                       };

        backend.Api.vkCreateComputePipeline(pipelineInfo, out var pipeline).CheckResult();
        return new VulkanPipeline(backend, pipeline, layout, setLayouts, VkPipelineBindPoint.Compute, declared, null);
    }

    /// <summary>
    /// One descriptor set per shader stage, as the compatibility layer binds them: D3D11 gives every stage its
    /// own slot space, so a vertex shader's t0 and a pixel shader's t0 have to end up in different sets.
    /// </summary>
    private static VkPipelineLayout CreateLayout(VulkanBackend backend, List<VulkanShader> shaders, out VkDescriptorSetLayout[] setLayouts,
                                                 out IReadOnlyDictionary<(int Set, int Slot), VkDescriptorType> declared)
    {
        var perSet = new SortedDictionary<int, Dictionary<int, (VkDescriptorType Type, VkShaderStageFlags Stages)>>();
        var types = new Dictionary<(int, int), VkDescriptorType>();

        foreach (var shader in shaders)
        {
            var stageFlags = VulkanConvert.ToVulkan(shader.Stage);

            foreach (var binding in shader.Bindings)
            {
                if (!perSet.TryGetValue(binding.Set, out var slots))
                {
                    slots = [];
                    perSet[binding.Set] = slots;
                }

                var type = VulkanConvert.ToVulkan(binding.Kind);

                // The same slot declared by two stages is one descriptor visible to both.
                slots[binding.Slot] = slots.TryGetValue(binding.Slot, out var existing)
                                          ? (existing.Type, existing.Stages | stageFlags)
                                          : (type, stageFlags);

                types[(binding.Set, binding.Slot)] = type;
            }
        }

        var highestSet = perSet.Count == 0 ? -1 : perSet.Keys.Max();
        setLayouts = new VkDescriptorSetLayout[highestSet + 1];

        for (var set = 0; set <= highestSet; set++)
        {
            perSet.TryGetValue(set, out var slots);
            slots ??= [];

            var bindings = stackalloc VkDescriptorSetLayoutBinding[Math.Max(1, slots.Count)];
            var index = 0;

            foreach (var (slot, entry) in slots)
            {
                bindings[index++] = new VkDescriptorSetLayoutBinding
                                        {
                                            binding = (uint)slot,
                                            descriptorType = entry.Type,
                                            descriptorCount = 1,
                                            stageFlags = entry.Stages,
                                        };
            }

            // Push descriptors: the bindings travel with the draw, so there is no pool to size and no set to
            // keep alive while the GPU reads it.
            VkDescriptorSetLayoutCreateInfo layoutInfo = new()
                                                             {
                                                                 flags = VkDescriptorSetLayoutCreateFlags.PushDescriptor,
                                                                 bindingCount = (uint)slots.Count,
                                                                 pBindings = slots.Count > 0 ? bindings : null,
                                                             };

            backend.Api.vkCreateDescriptorSetLayout(&layoutInfo, null, out setLayouts[set]).CheckResult();
        }

        fixed (VkDescriptorSetLayout* pointer = setLayouts)
        {
            VkPipelineLayoutCreateInfo layoutInfo = new()
                                                        {
                                                            setLayoutCount = (uint)setLayouts.Length,
                                                            pSetLayouts = setLayouts.Length > 0 ? pointer : null,
                                                        };

            backend.Api.vkCreatePipelineLayout(&layoutInfo, null, out var layout).CheckResult();
            declared = types;
            return layout;
        }
    }

    private static VkStencilOpState ToStencil(in StencilFaceState face, byte readMask, byte writeMask)
    {
        return new VkStencilOpState
                   {
                       failOp = VulkanConvert.ToVulkan(face.Fail),
                       passOp = VulkanConvert.ToVulkan(face.Pass),
                       depthFailOp = VulkanConvert.ToVulkan(face.DepthFail),
                       compareOp = VulkanConvert.ToVulkan(face.Compare),
                       compareMask = readMask,
                       writeMask = writeMask,
                   };
    }
}

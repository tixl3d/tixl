using System.Runtime.InteropServices;
using T3.Graphics;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace T3.Graphics.Vulkan;

/// <summary>
/// How the backend is created. Everything here has to be decided before the instance exists.
/// </summary>
public sealed record VulkanBackendOptions
{
    public bool EnableValidation { get; init; }

    /// <summary>
    /// Instance extensions a surface will need, from the window library. Empty for a headless backend, which
    /// can then create no swapchain.
    /// </summary>
    public IReadOnlyList<string> InstanceExtensions { get; init; } = [];
}

/// <summary>
/// The backend API on Vulkan 1.3: dynamic rendering, synchronization2 and push descriptors, which is what
/// lets a D3D11-shaped caller keep its "set some state, then draw" habits without a render pass or a
/// descriptor pool in sight.
/// </summary>
/// <remarks>
/// Headless — no surface and no swapchain. Presentation belongs to the window layer and comes with the
/// editor; everything below renders to textures and reads them back, which is also what the tests need.
/// </remarks>
public sealed unsafe class VulkanBackend : IGraphicsBackend, IDisposable
{
    public VulkanBackend(bool enableValidation = false) : this(new VulkanBackendOptions { EnableValidation = enableValidation })
    {
    }

    public VulkanBackend(VulkanBackendOptions options)
    {
        vkInitialize().CheckResult();

        var enableValidation = options.EnableValidation;
        var layers = new List<VkUtf8String>();

        // The window layer names what a surface needs on this platform — VK_KHR_surface plus a Wayland, Xlib
        // or Win32 one. The graphics layer cannot know which, and an instance cannot gain them later.
        var extensions = new List<string>(options.InstanceExtensions);

        var validationEnabled = enableValidation && IsLayerAvailable(ValidationLayerName);

        if (validationEnabled)
        {
            layers.Add(ValidationLayerName);
            // Spelled out: the constant is a span wrapper whose ToString is not the extension's name.
            extensions.Add("VK_EXT_debug_utils");
        }
        else if (enableValidation)
        {
            GraphicsLog.WarnOnce("The Vulkan validation layer is not installed; running without it.");
        }

        VkUtf8ReadOnlyString applicationName = "TiXL"u8;
        VkApplicationInfo applicationInfo = new()
                                                {
                                                    pApplicationName = applicationName,
                                                    pEngineName = applicationName,
                                                    apiVersion = VkVersion.Version_1_3,
                                                };

        using var layerNames = new VkStringArray(layers);
        using var extensionNames = new VkStringArray(extensions);

        VkInstanceCreateInfo instanceInfo = new()
                                                {
                                                    pApplicationInfo = &applicationInfo,
                                                    enabledLayerCount = layerNames.Length,
                                                    ppEnabledLayerNames = layerNames,
                                                    enabledExtensionCount = extensionNames.Length,
                                                    ppEnabledExtensionNames = extensionNames,
                                                };

        VkDebugUtilsMessengerCreateInfoEXT debugInfo = new()
                                                           {
                                                               messageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.Error
                                                                                 | VkDebugUtilsMessageSeverityFlagsEXT.Warning,
                                                               messageType = VkDebugUtilsMessageTypeFlagsEXT.General
                                                                             | VkDebugUtilsMessageTypeFlagsEXT.Validation
                                                                             | VkDebugUtilsMessageTypeFlagsEXT.Performance,
                                                               pfnUserCallback = &OnDebugMessage,
                                                           };

        // Attached to the create info as well, so errors during instance creation are reported too.
        if (validationEnabled)
            instanceInfo.pNext = &debugInfo;

        vkCreateInstance(&instanceInfo, out _instance).CheckResult();
        InstanceApi = GetApi(_instance);

        if (validationEnabled)
            InstanceApi.vkCreateDebugUtilsMessengerEXT(&debugInfo, null, out _debugMessenger).CheckResult();

        _debugNamesAvailable = validationEnabled;

        PhysicalDevice = PickPhysicalDevice(out _queueFamilyIndex, out var supportsMemoryBudget, out var supportsSwapchain);
        _supportsMemoryBudget = supportsMemoryBudget;
        _supportsSwapchain = supportsSwapchain;

        InstanceApi.vkGetPhysicalDeviceProperties(PhysicalDevice, out var properties);
        AdapterName = new VkUtf8String(properties.deviceName).ToString();
        _limits = properties.limits;

        var priority = 1f;
        VkDeviceQueueCreateInfo queueInfo = new()
                                                {
                                                    queueFamilyIndex = _queueFamilyIndex,
                                                    queueCount = 1,
                                                    pQueuePriorities = &priority,
                                                };

        List<VkUtf8String> deviceExtensions = [VK_KHR_PUSH_DESCRIPTOR_EXTENSION_NAME];

        if (supportsMemoryBudget)
            deviceExtensions.Add(VK_EXT_MEMORY_BUDGET_EXTENSION_NAME);

        // Only when the instance can make a surface at all: VK_KHR_swapchain requires VK_KHR_surface, and a
        // headless instance was not given it. A window that appears later needs the extensions up front,
        // which is why the caller passes them when it has a window in mind.
        if (_supportsSwapchain && options.InstanceExtensions.Count > 0)
            deviceExtensions.Add(VK_KHR_SWAPCHAIN_EXTENSION_NAME);
        else
            _supportsSwapchain = false;

        using var deviceExtensionNames = new VkStringArray(deviceExtensions);

        VkPhysicalDeviceVulkan13Features features13 = new() { dynamicRendering = true, synchronization2 = true };

        // scalarBlockLayout lets a structured buffer keep D3D's tight packing; shaderDrawParameters is what
        // makes SV_VertexID mean the same thing it does in D3D.
        VkPhysicalDeviceVulkan12Features features12 = new() { pNext = &features13, scalarBlockLayout = true };
        VkPhysicalDeviceVulkan11Features features11 = new() { pNext = &features12, shaderDrawParameters = true };
        VkPhysicalDeviceFeatures2 features2 = new() { pNext = &features11 };
        // D3D11 guarantees that reading past the end of a buffer returns zero, and TiXL's shaders rely on it -
        // a point or vertex index one past the last element is common. Vulkan leaves that undefined unless
        // this is on, and an undefined read faults the device rather than returning anything.
        features2.features.robustBufferAccess = true;
        features2.features.samplerAnisotropy = true;
        features2.features.depthClamp = true;
        features2.features.fillModeNonSolid = true;
        features2.features.independentBlend = true;
        features2.features.geometryShader = true;

        VkDeviceCreateInfo deviceInfo = new()
                                            {
                                                pNext = &features2,
                                                queueCreateInfoCount = 1,
                                                pQueueCreateInfos = &queueInfo,
                                                enabledExtensionCount = deviceExtensionNames.Length,
                                                ppEnabledExtensionNames = deviceExtensionNames,
                                            };

        InstanceApi.vkCreateDevice(PhysicalDevice, &deviceInfo, null, out _device).CheckResult();
        Api = GetApi(_instance, _device);
        Api.vkGetDeviceQueue(_queueFamilyIndex, 0, out _queue);

        VkCommandPoolCreateInfo poolInfo = new()
                                               {
                                                   flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
                                                   queueFamilyIndex = _queueFamilyIndex,
                                               };

        for (var i = 0; i < FramesInFlight; i++)
        {
            Api.vkCreateCommandPool(&poolInfo, null, out _frames[i].Pool).CheckResult();
            Api.vkAllocateCommandBuffer(_frames[i].Pool, out _frames[i].CommandBuffer).CheckResult();

            VkFenceCreateInfo fenceInfo = new() { flags = VkFenceCreateFlags.Signaled };
            Api.vkCreateFence(&fenceInfo, null, out _frames[i].Fence).CheckResult();

            VkSemaphoreCreateInfo semaphoreInfo = new();
            Api.vkCreateSemaphore(&semaphoreInfo, null, out _frames[i].ImageAvailable).CheckResult();
            _frames[i].Retired = [];
        }

        _commands = new VulkanCommandList(this);
    }

    internal readonly VkInstanceApi InstanceApi;
    internal readonly VkDeviceApi Api;
    internal readonly VkPhysicalDevice PhysicalDevice;

    public string AdapterName { get; }

    /// <summary>Zero: a VkDevice is not what the libraries asking for this expect.</summary>
    public IntPtr NativeDeviceHandle => IntPtr.Zero;

    /// <summary>
    /// Nothing to switch on. Vulkan lets any thread create resources and record into its own command buffer;
    /// what it does not allow is two threads sharing one, which the backend already avoids.
    /// </summary>
    public void SetMultithreadProtected(bool enabled)
    {
    }

    public GpuTexture? AdoptTexture(IntPtr nativeHandle, in TextureDescription description, string? label = null)
    {
        GraphicsLog.WarnOnce("Adopting a texture from another library needs an external-memory import, which is not implemented.");
        return null;
    }

    /// <summary>How many frames may be recorded before the oldest has to have finished.</summary>
    internal const int FramesInFlight = 2;

    #region resources
    public GpuTexture CreateTexture(in TextureDescription description, ReadOnlySpan<byte> initialData, string? label = null)
    {
        var format = VulkanConvert.ToVulkan(description.Format);

        // A staging texture is never bound, only copied to and mapped, so it is a buffer with a texture's
        // description — which is also how D3D11 uses it.
        if (description.Memory != MemoryKind.DeviceLocal)
        {
            var staging = CreateBufferCore(SizeOf(description), VkBufferUsageFlags.TransferSrc | VkBufferUsageFlags.TransferDst,
                                           description.Memory, out var stagingMemory, out var mapped);

            var stagingTexture = new VulkanTexture(this, VkImage.Null, stagingMemory, description, label) { StagingBuffer = staging, Mapped = mapped };

            if (!initialData.IsEmpty)
                initialData.CopyTo(new Span<byte>(mapped, initialData.Length));

            return stagingTexture;
        }

        VkImageCreateInfo imageInfo = new()
                                          {
                                              imageType = description.Dimension switch
                                                              {
                                                                  TextureDimension.Texture1D => VkImageType.Image1D,
                                                                  TextureDimension.Texture3D => VkImageType.Image3D,
                                                                  _                          => VkImageType.Image2D,
                                                              },
                                              format = format,
                                              extent = new VkExtent3D(Math.Max(1, description.Width), Math.Max(1, description.Height),
                                                                      description.Dimension == TextureDimension.Texture3D
                                                                          ? Math.Max(1, description.Depth)
                                                                          : 1),
                                              mipLevels = (uint)Math.Max(1, description.MipLevels),
                                              arrayLayers = (uint)Math.Max(1, description.Dimension == TextureDimension.TextureCube
                                                                                  ? Math.Max(6, description.ArraySize)
                                                                                  : description.ArraySize),
                                              samples = SupportedSampleCount(description),
                                              tiling = VkImageTiling.Optimal,
                                              usage = VulkanConvert.ToVulkan(description.Usage),
                                              sharingMode = VkSharingMode.Exclusive,
                                              initialLayout = VkImageLayout.Undefined,
                                              flags = description.Dimension == TextureDimension.TextureCube
                                                          ? VkImageCreateFlags.CubeCompatible
                                                          : VkImageCreateFlags.None,
                                          };

        if (Api.vkCreateImage(&imageInfo, null, out var image) != VkResult.Success)
        {
            ReportOutOfMemory($"texture {description.Width}x{description.Height} {description.Format}");
            return new VulkanTexture(this, VkImage.Null, VkDeviceMemory.Null, description, label);
        }

        Api.vkGetImageMemoryRequirements(image, out var requirements);

        if (!TryAllocate(requirements, VkMemoryPropertyFlags.DeviceLocal, out var memory))
        {
            Api.vkDestroyImage(image);
            ReportOutOfMemory($"texture {description.Width}x{description.Height} {description.Format}");
            return new VulkanTexture(this, VkImage.Null, VkDeviceMemory.Null, description, label);
        }

        Api.vkBindImageMemory(image, memory, 0).CheckResult();
        Interlocked.Add(ref _allocatedBytes, (long)requirements.size);

        var texture = new VulkanTexture(this, image, memory, description, label);
        SetDebugName(VkObjectType.Image, image.Handle, label);

        if (!initialData.IsEmpty)
            UploadTexture(texture, initialData);

        return texture;
    }

    public GpuTexture CreateTexture(in TextureDescription description, ReadOnlySpan<SubresourceData> initialData, string? label = null)
    {
        var texture = (VulkanTexture)CreateTexture(description, ReadOnlySpan<byte>.Empty, label);

        if (texture.Image.IsNull || initialData.Length == 0)
            return texture;

        // One staging buffer for all of it, then one copy per subresource: mip 0..n of slice 0, then slice 1,
        // in D3D11's order.
        var mipLevels = Math.Max(1, description.MipLevels);
        var arraySize = Math.Max(1, description.Dimension == TextureDimension.TextureCube ? Math.Max(6, description.ArraySize) : description.ArraySize);
        var regions = new VkBufferImageCopy[Math.Min(initialData.Length, mipLevels * arraySize)];

        // Every region's offset has to land on a texel boundary. The small end of a mip chain does not divide
        // evenly - a 640-wide image reaches a 10-byte level - so each subresource starts at an aligned offset
        // rather than wherever the previous one happened to end.
        var alignment = (ulong)Math.Max(1, FormatSizes.BytesPerPixel(description.Format));
        ulong total = 0;

        foreach (var subresource in initialData)
        {
            total = AlignUp(total, alignment) + (ulong)Math.Max(subresource.SlicePitch, subresource.RowPitch);
        }

        var staging = CreateBufferCore(total, VkBufferUsageFlags.TransferSrc, MemoryKind.Upload, out var stagingMemory, out var mapped);
        ulong offset = 0;

        for (var i = 0; i < regions.Length; i++)
        {
            var subresource = initialData[i];
            var size = Math.Max(subresource.SlicePitch, subresource.RowPitch);
            offset = AlignUp(offset, alignment);
            System.Buffer.MemoryCopy((void*)subresource.Data, (byte*)mapped + offset, size, size);

            var mip = i % mipLevels;
            var slice = i / mipLevels;
            var bytesPerPixel = Math.Max(1, FormatSizes.BytesPerPixel(description.Format));

            regions[i] = new VkBufferImageCopy
                             {
                                 bufferOffset = offset,

                                 // Row length is in texels, which is what a row pitch divides into.
                                 bufferRowLength = (uint)(subresource.RowPitch / bytesPerPixel),
                                 imageSubresource = new VkImageSubresourceLayers
                                                        {
                                                            aspectMask = texture.Aspect,
                                                            mipLevel = (uint)mip,
                                                            baseArrayLayer = (uint)slice,
                                                            layerCount = 1,
                                                        },
                                 imageExtent = new VkExtent3D(Math.Max(1, description.Width >> mip), Math.Max(1, description.Height >> mip),
                                                              description.Dimension == TextureDimension.Texture3D
                                                                  ? Math.Max(1, description.Depth >> mip)
                                                                  : 1),
                             };

            offset += (ulong)size;
        }

        SubmitOneShot(commandBuffer =>
                      {
                          VulkanBarriers.TransitionImage(this, commandBuffer, texture, VkImageLayout.TransferDstOptimal,
                                                         VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

                          fixed (VkBufferImageCopy* pointer = regions)
                          {
                              Api.vkCmdCopyBufferToImage(commandBuffer, staging, texture.Image, VkImageLayout.TransferDstOptimal,
                                                         (uint)regions.Length, pointer);
                          }

                          VulkanBarriers.TransitionImage(this, commandBuffer, texture, VkImageLayout.ShaderReadOnlyOptimal,
                                                         VkPipelineStageFlags2.FragmentShader, VkAccessFlags2.ShaderSampledRead);
                      });

        Api.vkDestroyBuffer(staging);
        Api.vkFreeMemory(stagingMemory);
        return texture;
    }

    public GpuBuffer CreateBuffer(in GpuBufferDescription description, ReadOnlySpan<byte> initialData, string? label = null)
    {
        var usage = VulkanConvert.ToVulkan(description.Usage);
        var size = (ulong)Math.Max(1, description.SizeInBytes);

        // Upload buffers hold one copy per frame in flight, so a write for this frame cannot land on memory a
        // previous frame is still reading. This is what D3D11 calls renaming and does inside Map(WriteDiscard).
        var slices = description.Memory == MemoryKind.Upload ? FramesInFlight : 1;
        var stride = AlignUp(size, BufferSliceAlignment(description.Usage));

        var buffer = CreateBufferCore(stride * (ulong)slices, usage, description.Memory, out var memory, out var mapped);

        if (buffer.IsNull)
        {
            ReportOutOfMemory($"buffer of {description.SizeInBytes} bytes");
            return new VulkanBuffer(this, VkBuffer.Null, VkDeviceMemory.Null, description, label);
        }

        var result = new VulkanBuffer(this, buffer, memory, description, label)
                         {
                             Mapped = mapped,
                             SliceCount = slices,
                             SliceStride = stride,
                         };

        SetDebugName(VkObjectType.Buffer, buffer.Handle, label);

        if (!initialData.IsEmpty)
        {
            if (mapped != null)
                initialData.CopyTo(new Span<byte>(mapped, initialData.Length));
            else
                UploadBuffer(result, initialData);
        }

        return result;
    }

    private VkBuffer CreateBufferCore(ulong size, VkBufferUsageFlags usage, MemoryKind memory, out VkDeviceMemory deviceMemory, out void* mapped)
    {
        deviceMemory = VkDeviceMemory.Null;
        mapped = null;

        if (memory != MemoryKind.DeviceLocal)
            usage |= VkBufferUsageFlags.TransferSrc | VkBufferUsageFlags.TransferDst;
        else
            usage |= VkBufferUsageFlags.TransferDst | VkBufferUsageFlags.TransferSrc;

        VkBufferCreateInfo bufferInfo = new()
                                            {
                                                size = Math.Max(1, size),
                                                usage = usage,
                                                sharingMode = VkSharingMode.Exclusive,
                                            };

        if (Api.vkCreateBuffer(&bufferInfo, null, out var buffer) != VkResult.Success)
            return VkBuffer.Null;

        Api.vkGetBufferMemoryRequirements(buffer, out var requirements);

        var properties = memory switch
                             {
                                 MemoryKind.Upload   => VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent,
                                 MemoryKind.Readback => VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent
                                                        | VkMemoryPropertyFlags.HostCached,
                                 _ => VkMemoryPropertyFlags.DeviceLocal,
                             };

        if (!TryAllocate(requirements, properties, out deviceMemory))
        {
            Api.vkDestroyBuffer(buffer);
            return VkBuffer.Null;
        }

        Api.vkBindBufferMemory(buffer, deviceMemory, 0).CheckResult();
        Interlocked.Add(ref _allocatedBytes, (long)requirements.size);

        if (memory != MemoryKind.DeviceLocal)
        {
            void* pointer;
            Api.vkMapMemory(deviceMemory, 0, VK_WHOLE_SIZE, VkMemoryMapFlags.None, &pointer).CheckResult();
            mapped = pointer;
        }

        return buffer;
    }

    public GpuTextureView CreateTextureView(GpuTexture texture, in TextureViewDescription description, string? label = null)
    {
        var source = (VulkanTexture)texture;

        if (source.Image.IsNull)
            return new VulkanTextureView(this, source, VkImageView.Null, description, label);

        var viewType = description.Dimension switch
                           {
                               TextureDimension.Texture1D   => description.ArraySize > 1 ? VkImageViewType.Image1DArray : VkImageViewType.Image1D,
                               TextureDimension.Texture3D   => VkImageViewType.Image3D,
                               TextureDimension.TextureCube => description.ArraySize > 6 ? VkImageViewType.ImageCubeArray : VkImageViewType.ImageCube,
                               _                            => description.ArraySize > 1 ? VkImageViewType.Image2DArray : VkImageViewType.Image2D,
                           };

        var format = description.Format == Format.Unknown ? source.VulkanFormat : VulkanConvert.ToVulkan(description.Format);

        // D3D11 samples a typeless depth buffer through a colour view - R32_Float over what it created as
        // R32_Typeless. Vulkan has neither the typeless format nor the reinterpretation: a view of a depth
        // image is a depth view, and the aspect already says so.
        if (VulkanConvert.IsDepth(source.Description.Format))
            format = source.VulkanFormat;

        VkImageViewCreateInfo viewInfo = new()
                                             {
                                                 image = source.Image,
                                                 viewType = viewType,
                                                 format = format,
                                                 subresourceRange = new VkImageSubresourceRange
                                                                        {
                                                                            aspectMask = source.Aspect,
                                                                            baseMipLevel = (uint)description.FirstMip,
                                                                            levelCount = description.MipCount == 0
                                                                                             ? VK_REMAINING_MIP_LEVELS
                                                                                             : (uint)description.MipCount,
                                                                            baseArrayLayer = (uint)description.FirstArraySlice,
                                                                            layerCount = description.Dimension == TextureDimension.Texture3D
                                                                                             ? 1
                                                                                             : (uint)Math.Max(1, description.ArraySize),
                                                                        },
                                             };

        Api.vkCreateImageView(&viewInfo, null, out var view).CheckResult();
        SetDebugName(VkObjectType.ImageView, view.Handle, label);
        return new VulkanTextureView(this, source, view, description, label);
    }

    public GpuSampler CreateSampler(in SamplerDescription description, string? label = null)
    {
        VkSamplerCreateInfo samplerInfo = new()
                                              {
                                                  magFilter = VulkanConvert.ToVulkan(description.MagFilter),
                                                  minFilter = VulkanConvert.ToVulkan(description.MinFilter),
                                                  mipmapMode = description.MipFilter == FilterMode.Linear
                                                                   ? VkSamplerMipmapMode.Linear
                                                                   : VkSamplerMipmapMode.Nearest,
                                                  addressModeU = VulkanConvert.ToVulkan(description.AddressU),
                                                  addressModeV = VulkanConvert.ToVulkan(description.AddressV),
                                                  addressModeW = VulkanConvert.ToVulkan(description.AddressW),
                                                  mipLodBias = description.MipLodBias,
                                                  anisotropyEnable = description.MaxAnisotropy > 1,
                                                  maxAnisotropy = Math.Clamp(description.MaxAnisotropy, 1, (int)_limits.maxSamplerAnisotropy),
                                                  compareEnable = description.Compare != null,
                                                  compareOp = VulkanConvert.ToVulkan(description.Compare ?? CompareFunction.Never),
                                                  minLod = description.MinLod,
                                                  maxLod = description.MaxLod <= 0 ? VK_LOD_CLAMP_NONE : description.MaxLod,
                                                  borderColor = VkBorderColor.FloatTransparentBlack,
                                              };

        Api.vkCreateSampler(&samplerInfo, null, out var sampler).CheckResult();
        SetDebugName(VkObjectType.Sampler, sampler.Handle, label);
        return new VulkanSampler(this, sampler, description, label);
    }

    public GpuShader CreateShader(ShaderStage stage, ReadOnlySpan<byte> code, string entryPoint, ReadOnlySpan<ShaderBinding> bindings = default,
                                  string? label = null)
    {
        fixed (byte* pointer = code)
        {
            VkShaderModuleCreateInfo moduleInfo = new()
                                                      {
                                                          codeSize = (nuint)code.Length,
                                                          pCode = (uint*)pointer,
                                                      };

            Api.vkCreateShaderModule(&moduleInfo, null, out var module).CheckResult();
            SetDebugName(VkObjectType.ShaderModule, module.Handle, label);
            return new VulkanShader(this, module, stage, bindings.ToArray(), label);
        }
    }
    #endregion

    #region pipelines
    public GpuSwapchain? CreateSwapchain(in SwapchainDescription description, in SurfaceTarget target, string? label = null)
    {
        if (!_supportsSwapchain || target.CreateVulkanSurface == null)
        {
            GraphicsLog.Error?.Invoke("This device cannot present, or the window did not provide a Vulkan surface.");
            return null;
        }

        var surface = new VkSurfaceKHR((ulong)target.CreateVulkanSurface(_instance.Handle));

        if (surface.IsNull)
        {
            GraphicsLog.Error?.Invoke("The window could not create a Vulkan surface.");
            return null;
        }

        InstanceApi.vkGetPhysicalDeviceSurfaceSupportKHR(PhysicalDevice, _queueFamilyIndex, surface, out var supported);

        if (!supported)
        {
            // Every driver TiXL targets presents from its graphics queue; a device that does not would need
            // a second queue and a transfer of ownership per frame.
            GraphicsLog.Error?.Invoke("The graphics queue of this GPU cannot present to the window.");
            InstanceApi.vkDestroySurfaceKHR(surface);
            return null;
        }

        return new VulkanSwapchain(this, surface, description, label);
    }

    public GpuPipeline GetOrCreatePipeline(in GraphicsPipelineDescription description)
    {
        lock (_pipelines)
        {
            if (_pipelines.TryGetValue(description, out var cached))
                return cached;

            var pipeline = VulkanPipelineFactory.CreateGraphics(this, description);
            _pipelines[description] = pipeline;
            return pipeline;
        }
    }

    public GpuPipeline GetOrCreatePipeline(in ComputePipelineDescription description)
    {
        lock (_computePipelines)
        {
            if (_computePipelines.TryGetValue(description, out var cached))
                return cached;

            var pipeline = VulkanPipelineFactory.CreateCompute(this, description);
            _computePipelines[description] = pipeline;
            return pipeline;
        }
    }

    public bool Supports(Topology topology)
    {
        // Triangle fans are optional in Vulkan and missing on some drivers; everything else is core.
        return topology != Topology.TriangleFan || _limits.maxDrawIndirectCount > 0;
    }

    public bool Supports(Format format, TextureUsage usage)
    {
        var vulkanFormat = VulkanConvert.ToVulkan(format);

        if (vulkanFormat == VkFormat.Undefined)
            return false;

        InstanceApi.vkGetPhysicalDeviceFormatProperties(PhysicalDevice, vulkanFormat, out var properties);
        var features = properties.optimalTilingFeatures;

        if ((usage & TextureUsage.Sampled) != 0 && (features & VkFormatFeatureFlags.SampledImage) == 0)
            return false;

        if ((usage & TextureUsage.RenderTarget) != 0 && (features & VkFormatFeatureFlags.ColorAttachment) == 0)
            return false;

        if ((usage & TextureUsage.DepthStencil) != 0 && (features & VkFormatFeatureFlags.DepthStencilAttachment) == 0)
            return false;

        if ((usage & TextureUsage.Storage) != 0 && (features & VkFormatFeatureFlags.StorageImage) == 0)
            return false;

        return true;
    }
    #endregion

    #region frames
    public ICommandList BeginFrame()
    {
        ref var frame = ref _frames[_frameIndex];

        var fence = frame.Fence;
        Api.vkWaitForFences(1, &fence, true, ulong.MaxValue).CheckResult();
        Api.vkResetFences(1, &fence).CheckResult();

        // Everything disposed while this slot's work was in flight is now unreferenced.
        RunRetired(ref frame);

        Api.vkResetCommandPool(frame.Pool, VkCommandPoolResetFlags.None).CheckResult();

        VkCommandBufferBeginInfo beginInfo = new() { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Api.vkBeginCommandBuffer(frame.CommandBuffer, &beginInfo).CheckResult();

        _commands.Begin(frame.CommandBuffer, _frameIndex);
        _frameActive = true;
        return _commands;
    }

    public void EndFrame(ICommandList commands)
    {
        if (!_frameActive)
            return;

        _commands.End();
        ref var frame = ref _frames[_frameIndex];
        Api.vkEndCommandBuffer(frame.CommandBuffer).CheckResult();

        var commandBuffer = frame.CommandBuffer;
        var imageAvailable = frame.ImageAvailable;
        var waitStage = VkPipelineStageFlags.ColorAttachmentOutput;

        // One signal per swapchain being presented, each belonging to the image it will present.
        var signalSemaphores = stackalloc VkSemaphore[Math.Max(1, _pendingPresents.Count)];

        for (var i = 0; i < _pendingPresents.Count; i++)
        {
            signalSemaphores[i] = _pendingPresents[i].RenderFinishedSemaphore;
        }

        VkSubmitInfo submitInfo = new()
                                      {
                                          commandBufferCount = 1,
                                          pCommandBuffers = &commandBuffer,
                                      };

        // Only a frame that acquired a swapchain image has anything to wait for or to signal.
        if (_acquiredSwapchains.Count > 0)
        {
            submitInfo.waitSemaphoreCount = 1;
            submitInfo.pWaitSemaphores = &imageAvailable;
            submitInfo.pWaitDstStageMask = &waitStage;
        }

        if (_pendingPresents.Count > 0)
        {
            submitInfo.signalSemaphoreCount = (uint)_pendingPresents.Count;
            submitInfo.pSignalSemaphores = signalSemaphores;
        }

        lock (_queueLock)
        {
            Api.vkQueueSubmit(_queue, 1, &submitInfo, frame.Fence).CheckResult();

            foreach (var swapchain in _pendingPresents)
            {
                swapchain.PresentNow(_queue, swapchain.RenderFinishedSemaphore);
            }
        }

        _pendingPresents.Clear();
        _acquiredSwapchains.Clear();
        _frameActive = false;
        _frameIndex = (_frameIndex + 1) % FramesInFlight;
    }

    internal VkSemaphore CurrentImageAvailableSemaphore => _frames[_frameIndex].ImageAvailable;

    internal void RegisterAcquiredSwapchain(VulkanSwapchain swapchain)
    {
        if (!_acquiredSwapchains.Contains(swapchain))
            _acquiredSwapchains.Add(swapchain);
    }

    internal void QueuePresent(VulkanSwapchain swapchain)
    {
        if (!_pendingPresents.Contains(swapchain))
            _pendingPresents.Add(swapchain);
    }

    /// <summary>Puts a back buffer into the layout the window system expects, inside the frame's commands.</summary>
    internal void TransitionToPresent(VulkanTexture texture)
    {
        if (!_frameActive)
            return;

        _commands.TransitionToPresent(texture);
    }

    /// <summary>Which frame slot is being recorded. Upload buffers use it to pick the copy they may write.</summary>
    internal int CurrentFrameIndex => _frameIndex;

    /// <summary>
    /// Hands work to the slot that is currently recording: by the time that slot comes round again its fence
    /// has been waited on, so nothing the GPU could still be reading is freed.
    /// </summary>
    internal void Retire(Action<VkDeviceApi> release)
    {
        lock (_frames)
        {
            _frames[_frameIndex].Retired.Add(release);
        }
    }

    private void RunRetired(ref FrameSlot frame)
    {
        List<Action<VkDeviceApi>> retired;

        lock (_frames)
        {
            if (frame.Retired.Count == 0)
                return;

            retired = [..frame.Retired];
            frame.Retired.Clear();
        }

        foreach (var release in retired)
        {
            release(Api);
        }
    }

    /// <summary>Submits whatever is recorded and blocks. The blocking map and readback paths need it.</summary>
    internal void FlushAndWait()
    {
        if (_frameActive)
        {
            var current = _frameIndex;
            EndFrame(_commands);

            var fence = _frames[current].Fence;
            Api.vkWaitForFences(1, &fence, true, ulong.MaxValue).CheckResult();
        }
        else
        {
            lock (_queueLock)
            {
                Api.vkQueueWaitIdle(_queue).CheckResult();
            }
        }
    }
    #endregion

    #region mapping and readback
    public MappedMemory MapForRead(GpuResource resource, int subresource)
    {
        // D3D11's Map(Read) waits for the GPU, and the operators that use it are written around that stall.
        FlushAndWait();

        switch (resource)
        {
            case VulkanBuffer buffer when buffer.Mapped != null:
                return new MappedMemory(buffer.Mapped, buffer.Description.SizeInBytes, buffer.Description.SizeInBytes,
                                        buffer.Description.SizeInBytes);

            case VulkanTexture texture when texture.Mapped != null:
            {
                var rowPitch = texture.Description.Width * FormatSizes.BytesPerPixel(texture.Description.Format);
                var size = SizeOf(texture.Description);
                return new MappedMemory(texture.Mapped, rowPitch, (int)size, (int)size);
            }

            default:
                GraphicsLog.WarnOnce("Only resources created with readback or upload memory can be mapped.");
                return default;
        }
    }

    public void Unmap(GpuResource resource, int subresource)
    {
        // Host-visible memory stays mapped for the resource's lifetime; there is nothing to undo.
    }

    public Task<ReadbackResult> ReadbackAsync(GpuTexture texture, int mipLevel, int arraySlice, CancellationToken cancellation = default)
    {
        if (texture is not VulkanTexture source || source.Image.IsNull)
            return Task.FromResult(new ReadbackResult(ReadOnlyMemory<byte>.Empty, 0, 0, null));

        var description = texture.Description with { Memory = MemoryKind.Readback, Usage = TextureUsage.CopyDestination };
        using var staging = (VulkanTexture)CreateTexture(description, ReadOnlySpan<byte>.Empty, "readback");

        var commands = _frameActive ? _commands : (VulkanCommandList)BeginFrame();
        commands.CopyTexture(source, staging);
        FlushAndWait();

        var size = (int)SizeOf(description);
        var bytes = new byte[size];
        new Span<byte>(staging.Mapped, size).CopyTo(bytes);

        // Completed, not pending: the copy is already done. Vulkan could signal a fence instead and let the
        // caller carry on, which is what this turns into once a frame graph decides when to wait.
        return Task.FromResult(new ReadbackResult(bytes, description.Width * FormatSizes.BytesPerPixel(description.Format), size, null));
    }

    public Task<ReadbackResult> ReadbackAsync(GpuBuffer buffer, CancellationToken cancellation = default)
    {
        if (buffer is not VulkanBuffer source)
            return Task.FromResult(new ReadbackResult(ReadOnlyMemory<byte>.Empty, 0, 0, null));

        var description = buffer.Description with { Memory = MemoryKind.Readback, Usage = BufferUsage.CopyDestination };
        using var staging = (VulkanBuffer)CreateBuffer(description, ReadOnlySpan<byte>.Empty, "readback");

        var commands = _frameActive ? _commands : (VulkanCommandList)BeginFrame();
        commands.CopyBuffer(source, 0, staging, 0, buffer.Description.SizeInBytes);
        FlushAndWait();

        var bytes = new byte[buffer.Description.SizeInBytes];
        new Span<byte>(staging.Mapped, bytes.Length).CopyTo(bytes);

        return Task.FromResult(new ReadbackResult(bytes, bytes.Length, bytes.Length, null));
    }
    #endregion

    #region memory
    public MemoryReport QueryMemory()
    {
        var resourceBytes = Interlocked.Read(ref _allocatedBytes);

        if (!_supportsMemoryBudget)
            return new MemoryReport(0, resourceBytes, resourceBytes);

        VkPhysicalDeviceMemoryBudgetPropertiesEXT budget = new();
        VkPhysicalDeviceMemoryProperties2 properties = new() { pNext = &budget };
        InstanceApi.vkGetPhysicalDeviceMemoryProperties2(PhysicalDevice, &properties);

        long budgetBytes = 0;
        long usedBytes = 0;

        for (var heap = 0; heap < properties.memoryProperties.memoryHeapCount; heap++)
        {
            if ((properties.memoryProperties.memoryHeaps[heap].flags & VkMemoryHeapFlags.DeviceLocal) == 0)
                continue;

            budgetBytes += (long)budget.heapBudget[heap];
            usedBytes += (long)budget.heapUsage[heap];
        }

        var report = new MemoryReport(budgetBytes, usedBytes, resourceBytes);

        if (report.Pressure > MemoryPressureThreshold)
            MemoryPressure?.Invoke(report);

        return report;
    }

    public event Action<MemoryReport>? MemoryPressure;

    private void ReportOutOfMemory(string what)
    {
        var report = QueryMemory();
        GraphicsLog.Error?.Invoke($"Out of GPU memory creating {what} ({report.UsedBytes >> 20} MB of {report.BudgetBytes >> 20} MB used).");
        MemoryPressure?.Invoke(report);
    }

    private bool TryAllocate(in VkMemoryRequirements requirements, VkMemoryPropertyFlags properties, out VkDeviceMemory memory)
    {
        memory = VkDeviceMemory.Null;

        if (!TryFindMemoryType(requirements.memoryTypeBits, properties, out var typeIndex))
        {
            // A device-local allocation that does not fit can still live in host memory: slower, but the
            // alternative is a black frame. D3D11 did this silently.
            if ((properties & VkMemoryPropertyFlags.DeviceLocal) == 0
                || !TryFindMemoryType(requirements.memoryTypeBits, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent,
                                      out typeIndex))
            {
                return false;
            }

            GraphicsLog.WarnOnce("A device-local allocation fell back to host memory; the GPU budget is exhausted.");
        }

        VkMemoryAllocateInfo allocateInfo = new()
                                                {
                                                    allocationSize = requirements.size,
                                                    memoryTypeIndex = typeIndex,
                                                };

        // One allocation per resource. Drivers cap the number of allocations (often 4096), so a sub-allocator
        // has to come before a scene with thousands of textures.
        return Api.vkAllocateMemory(&allocateInfo, null, out memory) == VkResult.Success;
    }

    private bool TryFindMemoryType(uint typeBits, VkMemoryPropertyFlags required, out uint typeIndex)
    {
        InstanceApi.vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, out var properties);

        for (var index = 0; index < properties.memoryTypeCount; index++)
        {
            if ((typeBits & (1u << index)) == 0)
                continue;

            if ((properties.memoryTypes[index].propertyFlags & required) != required)
                continue;

            typeIndex = (uint)index;
            return true;
        }

        typeIndex = 0;
        return false;
    }

    private ulong BufferSliceAlignment(BufferUsage usage)
    {
        var alignment = 16ul;

        if ((usage & BufferUsage.Constant) != 0)
            alignment = Math.Max(alignment, _limits.minUniformBufferOffsetAlignment);

        if ((usage & (BufferUsage.Structured | BufferUsage.Storage)) != 0)
            alignment = Math.Max(alignment, _limits.minStorageBufferOffsetAlignment);

        return alignment;
    }

    private static ulong AlignUp(ulong value, ulong alignment) => (value + alignment - 1) / alignment * alignment;

    internal static ulong SizeOf(in TextureDescription description)
    {
        var bytesPerPixel = FormatSizes.BytesPerPixel(description.Format);

        if (bytesPerPixel == 0)
            bytesPerPixel = 4;

        return (ulong)(Math.Max(1, description.Width) * Math.Max(1, description.Height) * Math.Max(1, description.Depth == 0 ? 1 : description.Depth)
                       * bytesPerPixel);
    }

    private const float MemoryPressureThreshold = 0.9f;
    #endregion

    #region uploads
    /// <summary>
    /// Uploads through a one-shot command buffer of its own, so a texture can be created on a loader thread
    /// while the main thread is recording a frame.
    /// </summary>
    private void UploadTexture(VulkanTexture texture, ReadOnlySpan<byte> data)
    {
        var staging = CreateBufferCore((ulong)data.Length, VkBufferUsageFlags.TransferSrc, MemoryKind.Upload, out var stagingMemory, out var mapped);
        data.CopyTo(new Span<byte>(mapped, data.Length));

        SubmitOneShot(commandBuffer =>
                      {
                          VulkanBarriers.TransitionImage(this, commandBuffer, texture, VkImageLayout.TransferDstOptimal,
                                                         VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);

                          VkBufferImageCopy region = new()
                                                         {
                                                             imageSubresource = new VkImageSubresourceLayers
                                                                                    {
                                                                                        aspectMask = texture.Aspect,
                                                                                        mipLevel = 0,
                                                                                        baseArrayLayer = 0,
                                                                                        layerCount = 1,
                                                                                    },
                                                             imageExtent = new VkExtent3D(Math.Max(1, texture.Description.Width),
                                                                                          Math.Max(1, texture.Description.Height),
                                                                                          texture.Description.Dimension == TextureDimension.Texture3D
                                                                                              ? Math.Max(1, texture.Description.Depth)
                                                                                              : 1),
                                                         };

                          Api.vkCmdCopyBufferToImage(commandBuffer, staging, texture.Image, VkImageLayout.TransferDstOptimal, 1, &region);

                          VulkanBarriers.TransitionImage(this, commandBuffer, texture, VkImageLayout.ShaderReadOnlyOptimal,
                                                         VkPipelineStageFlags2.FragmentShader, VkAccessFlags2.ShaderSampledRead);
                      });

        Api.vkDestroyBuffer(staging);
        Api.vkFreeMemory(stagingMemory);
    }

    private void UploadBuffer(VulkanBuffer buffer, ReadOnlySpan<byte> data)
    {
        var staging = CreateBufferCore((ulong)data.Length, VkBufferUsageFlags.TransferSrc, MemoryKind.Upload, out var stagingMemory, out var mapped);
        data.CopyTo(new Span<byte>(mapped, data.Length));

        var length = (ulong)data.Length;
        SubmitOneShot(commandBuffer =>
                      {
                          VkBufferCopy region = new() { size = length };
                          Api.vkCmdCopyBuffer(commandBuffer, staging, buffer.Buffer, 1, &region);
                      });

        Api.vkDestroyBuffer(staging);
        Api.vkFreeMemory(stagingMemory);
    }

    internal void SubmitOneShot(Action<VkCommandBuffer> record)
    {
        VkCommandPoolCreateInfo poolInfo = new()
                                               {
                                                   flags = VkCommandPoolCreateFlags.Transient,
                                                   queueFamilyIndex = _queueFamilyIndex,
                                               };

        Api.vkCreateCommandPool(&poolInfo, null, out var pool).CheckResult();
        Api.vkAllocateCommandBuffer(pool, out var commandBuffer).CheckResult();

        VkCommandBufferBeginInfo beginInfo = new() { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Api.vkBeginCommandBuffer(commandBuffer, &beginInfo).CheckResult();
        record(commandBuffer);
        Api.vkEndCommandBuffer(commandBuffer).CheckResult();

        VkSubmitInfo submitInfo = new() { commandBufferCount = 1, pCommandBuffers = &commandBuffer };

        // Resource creation is allowed on any thread, but a queue may only be used by one at a time.
        lock (_queueLock)
        {
            Api.vkQueueSubmit(_queue, 1, &submitInfo, VkFence.Null).CheckResult();
            Api.vkQueueWaitIdle(_queue).CheckResult();
        }

        Api.vkDestroyCommandPool(pool);
    }
    #endregion

    /// <summary>Created on first use: a draw can only need them once a pipeline exists.</summary>
    internal VulkanNullResources NullResources => _nullResources ??= new VulkanNullResources(this);

    #region imgui textures
    internal ulong RegisterImGuiTexture(VulkanTextureView view)
    {
        lock (_imGuiTextures)
        {
            var id = ++_nextImGuiTextureId;
            _imGuiTextures[id] = view;
            return id;
        }
    }

    internal void UnregisterImGuiTexture(ulong id)
    {
        lock (_imGuiTextures)
        {
            _imGuiTextures.Remove(id);
        }
    }

    /// <summary>Null for a view that died between building the draw list and drawing it, which draws nothing.</summary>
    public bool TryResolveImGuiTexture(ulong id, out object? view)
    {
        lock (_imGuiTextures)
        {
            var found = _imGuiTextures.TryGetValue(id, out var result);
            view = result;
            return found;
        }
    }
    #endregion

    internal void SetDebugName(VkObjectType type, ulong handle, string? name)
    {
        if (name == null || !_debugNamesAvailable)
            return;

        var bytes = System.Text.Encoding.UTF8.GetBytes(name + '\0');

        fixed (byte* pointer = bytes)
        {
            VkDebugUtilsObjectNameInfoEXT nameInfo = new()
                                                         {
                                                             objectType = type,
                                                             objectHandle = handle,
                                                             pObjectName = pointer,
                                                         };

            InstanceApi.vkSetDebugUtilsObjectNameEXT(_device, &nameInfo);
        }
    }

    /// <summary>
    /// How many validation errors this process has seen. Zero is the only acceptable value in a test; a
    /// backend that renders the right picture while validation complains is a backend that breaks on the
    /// next driver.
    /// </summary>
    public static int ValidationErrorCount => _validationErrorCount;

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static uint OnDebugMessage(VkDebugUtilsMessageSeverityFlagsEXT severity, VkDebugUtilsMessageTypeFlagsEXT types,
                                       VkDebugUtilsMessengerCallbackDataEXT* callbackData, void* userData)
    {
        var message = new VkUtf8String(callbackData->pMessage).ToString();

        if ((severity & VkDebugUtilsMessageSeverityFlagsEXT.Error) != 0)
        {
            Interlocked.Increment(ref _validationErrorCount);
            GraphicsLog.Error?.Invoke($"[Vulkan] {message}");
        }
        else
        {
            GraphicsLog.Warning?.Invoke($"[Vulkan] {message}");
        }

        return VK_FALSE;
    }

    private static int _validationErrorCount;

    /// <summary>
    /// The requested sample count, reduced to one the device offers for the ways this image will be used.
    /// An unsupported count is invalid usage, and RADV faults on it rather than refusing the image.
    /// </summary>
    private VkSampleCountFlags SupportedSampleCount(in TextureDescription description)
    {
        var requested = ToSampleCount(description.Samples.Count);

        if (requested == VkSampleCountFlags.Count1)
            return requested;

        var allowed = (VkSampleCountFlags)0x7f;

        if ((description.Usage & TextureUsage.RenderTarget) != 0)
            allowed &= _limits.framebufferColorSampleCounts;

        if ((description.Usage & TextureUsage.DepthStencil) != 0)
            allowed &= _limits.framebufferDepthSampleCounts;

        if ((description.Usage & TextureUsage.Sampled) != 0)
            allowed &= _limits.sampledImageColorSampleCounts;

        if ((description.Usage & TextureUsage.Storage) != 0)
            allowed &= _limits.storageImageSampleCounts;

        while (requested > VkSampleCountFlags.Count1 && (allowed & requested) == 0)
        {
            requested = (VkSampleCountFlags)((uint)requested >> 1);
        }

        if (requested < ToSampleCount(description.Samples.Count))
            GraphicsLog.WarnOnce($"{description.Samples.Count}x multisampling is not available here; using {(uint)requested}x.");

        return requested == 0 ? VkSampleCountFlags.Count1 : requested;
    }

    private static VkSampleCountFlags ToSampleCount(int count)
    {
        return count switch
                   {
                       >= 16 => VkSampleCountFlags.Count16,
                       >= 8  => VkSampleCountFlags.Count8,
                       >= 4  => VkSampleCountFlags.Count4,
                       >= 2  => VkSampleCountFlags.Count2,
                       _     => VkSampleCountFlags.Count1,
                   };
    }

    private VkPhysicalDevice PickPhysicalDevice(out uint queueFamilyIndex, out bool supportsMemoryBudget, out bool supportsSwapchain)
    {
        uint deviceCount = 0;
        InstanceApi.vkEnumeratePhysicalDevices(&deviceCount, null).CheckResult();

        if (deviceCount == 0)
            throw new InvalidOperationException("No GPU with Vulkan support found.");

        var devices = new VkPhysicalDevice[deviceCount];
        InstanceApi.vkEnumeratePhysicalDevices(devices).CheckResult();

        var best = VkPhysicalDevice.Null;
        queueFamilyIndex = 0;
        supportsMemoryBudget = false;
        supportsSwapchain = false;

        foreach (var candidate in devices)
        {
            if (!SupportsRequiredFeatures(candidate, out var hasMemoryBudget, out var hasSwapchain) || !TryFindGraphicsQueue(candidate, out var family))
                continue;

            InstanceApi.vkGetPhysicalDeviceProperties(candidate, out var properties);
            var isDiscrete = properties.deviceType == VkPhysicalDeviceType.DiscreteGpu;

            if (best.IsNull || isDiscrete)
            {
                best = candidate;
                queueFamilyIndex = family;
                supportsMemoryBudget = hasMemoryBudget;
                supportsSwapchain = hasSwapchain;

                if (isDiscrete)
                    break;
            }
        }

        if (best.IsNull)
        {
            throw new InvalidOperationException("No GPU supports Vulkan 1.3 with dynamic rendering, synchronization2, "
                                                + "scalar block layout, shader draw parameters and push descriptors.");
        }

        return best;
    }

    private bool SupportsRequiredFeatures(VkPhysicalDevice physicalDevice, out bool supportsMemoryBudget, out bool supportsSwapchain)
    {
        supportsMemoryBudget = false;
        supportsSwapchain = false;

        InstanceApi.vkGetPhysicalDeviceProperties(physicalDevice, out var properties);

        if (properties.apiVersion < VkVersion.Version_1_3)
            return false;

        VkPhysicalDeviceVulkan13Features features13 = new();
        VkPhysicalDeviceVulkan12Features features12 = new() { pNext = &features13 };
        VkPhysicalDeviceVulkan11Features features11 = new() { pNext = &features12 };
        VkPhysicalDeviceFeatures2 features2 = new() { pNext = &features11 };
        InstanceApi.vkGetPhysicalDeviceFeatures2(physicalDevice, &features2);

        if (!features13.dynamicRendering || !features13.synchronization2 || !features12.scalarBlockLayout || !features11.shaderDrawParameters)
            return false;

        uint extensionCount = 0;
        InstanceApi.vkEnumerateDeviceExtensionProperties(physicalDevice, null, &extensionCount, null).CheckResult();
        var extensions = new VkExtensionProperties[extensionCount];

        fixed (VkExtensionProperties* pointer = extensions)
        {
            InstanceApi.vkEnumerateDeviceExtensionProperties(physicalDevice, null, &extensionCount, pointer).CheckResult();
        }

        var hasPushDescriptor = false;

        for (var index = 0; index < extensions.Length; index++)
        {
            fixed (byte* namePointer = extensions[index].extensionName)
            {
                var name = new VkUtf8String(namePointer);
                hasPushDescriptor |= name == VK_KHR_PUSH_DESCRIPTOR_EXTENSION_NAME;
                supportsMemoryBudget |= name == VK_EXT_MEMORY_BUDGET_EXTENSION_NAME;
                supportsSwapchain |= name == VK_KHR_SWAPCHAIN_EXTENSION_NAME;
            }
        }

        return hasPushDescriptor;
    }

    private bool TryFindGraphicsQueue(VkPhysicalDevice physicalDevice, out uint familyIndex)
    {
        uint familyCount = 0;
        InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &familyCount, null);
        var families = new VkQueueFamilyProperties[familyCount];

        fixed (VkQueueFamilyProperties* pointer = families)
        {
            InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &familyCount, pointer);
        }

        for (familyIndex = 0; familyIndex < familyCount; familyIndex++)
        {
            var flags = families[familyIndex].queueFlags;

            if ((flags & VkQueueFlags.Graphics) != 0 && (flags & VkQueueFlags.Compute) != 0)
                return true;
        }

        return false;
    }

    private static bool IsLayerAvailable(VkUtf8String layerName)
    {
        uint layerCount = 0;
        vkEnumerateInstanceLayerProperties(&layerCount, null).CheckResult();
        var layers = new VkLayerProperties[layerCount];
        vkEnumerateInstanceLayerProperties(layers).CheckResult();

        for (var index = 0; index < layers.Length; index++)
        {
            fixed (byte* namePointer = layers[index].layerName)
            {
                if (new VkUtf8String(namePointer) == layerName)
                    return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        Api.vkDeviceWaitIdle();
        _nullResources?.Dispose();

        foreach (var pipeline in _pipelines.Values)
        {
            pipeline.Dispose();
        }

        foreach (var pipeline in _computePipelines.Values)
        {
            pipeline.Dispose();
        }

        for (var i = 0; i < FramesInFlight; i++)
        {
            RunRetired(ref _frames[i]);
            Api.vkDestroyFence(_frames[i].Fence);
            Api.vkDestroySemaphore(_frames[i].ImageAvailable);
            Api.vkDestroyCommandPool(_frames[i].Pool);
        }

        Api.vkDestroyDevice();

        if (!_debugMessenger.IsNull)
            InstanceApi.vkDestroyDebugUtilsMessengerEXT(_debugMessenger);

        InstanceApi.vkDestroyInstance();
    }

    private struct FrameSlot
    {
        internal VkCommandPool Pool;
        internal VkCommandBuffer CommandBuffer;
        internal VkFence Fence;

        /// <summary>
        /// Signalled when a swapchain image is ready to be rendered into. Per frame rather than per image,
        /// because the fence of the slot it belongs to has been waited on before it is used again.
        /// </summary>
        internal VkSemaphore ImageAvailable;
        internal List<Action<VkDeviceApi>> Retired;
    }

    private static readonly VkUtf8String ValidationLayerName = "VK_LAYER_KHRONOS_validation"u8;

    private readonly VkInstance _instance;
    private readonly VkDevice _device;
    private readonly VkQueue _queue;
    private readonly uint _queueFamilyIndex;
    private readonly VkPhysicalDeviceLimits _limits;
    private readonly bool _supportsMemoryBudget;
    private bool _supportsSwapchain;
    private readonly bool _debugNamesAvailable;
    private readonly VkDebugUtilsMessengerEXT _debugMessenger = VkDebugUtilsMessengerEXT.Null;
    private readonly object _queueLock = new();
    private readonly VulkanCommandList _commands;
    private readonly FrameSlot[] _frames = new FrameSlot[FramesInFlight];
    private readonly Dictionary<GraphicsPipelineDescription, VulkanPipeline> _pipelines = [];
    private readonly Dictionary<ComputePipelineDescription, VulkanPipeline> _computePipelines = [];
    private readonly Dictionary<ulong, VulkanTextureView> _imGuiTextures = [];
    private readonly List<VulkanSwapchain> _pendingPresents = [];
    private readonly List<VulkanSwapchain> _acquiredSwapchains = [];

    private VulkanNullResources? _nullResources;
    private long _allocatedBytes;
    private ulong _nextImGuiTextureId;
    private int _frameIndex;
    private bool _frameActive;
}

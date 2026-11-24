using T3.Graphics;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace T3.Graphics.Vulkan;

/// <summary>
/// Presentation. Unlike D3D11, where Present is one call, Vulkan has to acquire an image before the frame
/// renders and present it after that frame's work is submitted — so acquiring happens when the back buffer is
/// first touched, and the present itself is queued for the end of the frame.
/// </summary>
internal sealed unsafe class VulkanSwapchain : GpuSwapchain
{
    internal VulkanSwapchain(VulkanBackend backend, VkSurfaceKHR surface, in SwapchainDescription description, string? label)
        : base(description, label)
    {
        _backend = backend;
        _surface = surface;

        // Each swapchain needs its own: two windows acquiring in the same frame on one shared semaphore signal it
        // twice, and the frame's submit only waits on it once.
        VkSemaphoreCreateInfo semaphoreInfo = new();
        for (var i = 0; i < _imageAvailable.Length; i++)
        {
            backend.Api.vkCreateSemaphore(&semaphoreInfo, null, out _imageAvailable[i]).CheckResult();
        }

        _supportedPresentModes = QuerySupportedPresentModes();
        Create(description.Width, description.Height);
    }

    public override GpuTexture BackBuffer
    {
        get
        {
            AcquireIfNeeded();
            return _images[_imageIndex];
        }
    }

    public override void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == Description.Width && height == Description.Height))
            return;

        // An acquired image has signalled its semaphore, and only the submit that waits on it clears it.
        // Recreating now would abandon the acquisition and leave that semaphore signalled for the next acquire
        // to trip over, so an image already in flight is presented at the old size and the resize follows.
        if (_acquired)
        {
            _requestedWidth = width;
            _requestedHeight = height;
            _needsRecreation = true;
            return;
        }

        _backend.Api.vkDeviceWaitIdle();
        DestroyImagesAndSwapchain();
        Create(width, height);
    }

    public override void Present(int syncInterval)
    {
        if (_swapchain.IsNull || !_acquired)
            return;

        // The image has to be in the present layout before the frame is submitted, and this is still inside
        // the frame's command buffer.
        _backend.TransitionToPresent(_images[_imageIndex]);

        // Vulkan bakes vsync into the swapchain, so switching it means rebuilding: D3D11 takes the interval
        // per present, and the Player's vsync toggle expects to be able to change it while running.
        _presentSyncInterval = syncInterval;

        if (PickPresentMode() != _presentMode)
            _needsRecreation = true;

        _backend.QueuePresent(this);
    }

    /// <summary>
    /// Takes the frame's two blocking waits up front, where D3D11's latency waitable blocks: the fence of the
    /// frame slot about to be reused, then the image acquire, which is where FIFO paces the loop to the display.
    /// Left to happen lazily they block halfway through the frame, after its input was read, and the vsync wait
    /// shows up as CPU time.
    /// </summary>
    public override void WaitForFrameLatency()
    {
        if (_acquired)
            return;

        // A present reaching the display is the one wait that paces the loop to the refresh; the two below then
        // rarely block. One present stays in flight: a queued present is shown a vblank later, so waiting for the
        // latest one halves the frame rate. A timeout (display off, window hidden) just lets the frame go ahead.
        // Without vsync the loop runs free, so there is nothing to pace to.
        if (_lastPresentId > 1 && _presentSyncInterval > 0 && !_swapchain.IsNull && _backend.SupportsPresentWait)
            _backend.Api.vkWaitForPresentKHR(_swapchain, _lastPresentId - 1, PresentWaitTimeoutNs);

        // The acquire signals this slot's semaphore, which is only free again once the slot's last frame is done.
        _backend.WaitForRecordingSlot();
        AcquireIfNeeded();
    }

    /// <summary>Presents the acquired image. Called by the backend once the frame's work has been submitted.</summary>
    internal VkResult PresentNow(VkQueue queue, VkSemaphore waitSemaphore)
    {
        var swapchain = _swapchain;
        var imageIndex = (uint)_imageIndex;
        var semaphore = waitSemaphore;

        // Numbered so the next frame can wait for this present with VK_KHR_present_wait.
        var presentId = _lastPresentId + 1;
        VkPresentIdKHR presentIdInfo = new() { swapchainCount = 1, pPresentIds = &presentId };

        VkPresentInfoKHR presentInfo = new()
                                           {
                                               pNext = _backend.SupportsPresentWait ? &presentIdInfo : null,
                                               waitSemaphoreCount = semaphore.IsNull ? 0u : 1u,
                                               pWaitSemaphores = semaphore.IsNull ? null : &semaphore,
                                               swapchainCount = 1,
                                               pSwapchains = &swapchain,
                                               pImageIndices = &imageIndex,
                                           };

        // Asks the display to report when this frame's first pixel went out; ReadShownFrames collects it.
        VkPresentTimingInfoEXT timingInfo = new()
                                                {
                                                    timeDomainId = _timingDomainId,
                                                    presentStageQueries = _shownStage,
                                                    targetTimeDomainPresentStage = _shownStage,
                                                };
        VkPresentTimingsInfoEXT timingsInfo = new() { swapchainCount = 1, pTimingInfos = &timingInfo };
        VkPresentId2KHR presentId2Info = new() { pNext = &timingsInfo, swapchainCount = 1, pPresentIds = &presentId };
        if (_reportsShownFrames)
            presentIdInfo.pNext = &presentId2Info;

        var result = _backend.Api.vkQueuePresentKHR(queue, &presentInfo);

        // Only if reports were left unread: the frame is still worth showing without one.
        if (result == VkResult.ErrorPresentTimingQueueFullEXT)
        {
            presentIdInfo.pNext = null;
            result = _backend.Api.vkQueuePresentKHR(queue, &presentInfo);
        }

        if (result is VkResult.Success or VkResult.SuboptimalKHR)
            _lastPresentId = presentId;
        _acquired = false;

        // A window that changed size mid-frame is not an error; the next frame renders at the new size.
        if (result is VkResult.ErrorOutOfDateKHR or VkResult.SuboptimalKHR)
            _needsRecreation = true;

        return result;
    }

    public override int ReadShownFrames(Span<ShownFrame> frames)
    {
        if (!_reportsShownFrames || _swapchain.IsNull || frames.Length == 0)
            return 0;

        var capacity = Math.Min(frames.Length, PresentTimingQueueSize);
        var stages = stackalloc VkPresentStageTimeEXT[capacity];
        var timings = stackalloc VkPastPresentationTimingEXT[capacity];
        for (var index = 0; index < capacity; index++)
        {
            timings[index] = new VkPastPresentationTimingEXT { presentStageCount = 1, pPresentStages = &stages[index] };
        }

        VkPastPresentationTimingInfoEXT info = new() { swapchain = _swapchain };
        VkPastPresentationTimingPropertiesEXT properties = new() { presentationTimingCount = (uint)capacity, pPresentationTimings = timings };
        var result = _backend.Api.vkGetPastPresentationTimingEXT(&info, &properties);
        if (result is not (VkResult.Success or VkResult.Incomplete))
            return 0;

        var written = 0;
        for (var index = 0; index < properties.presentationTimingCount && index < capacity; index++)
        {
            ref var timing = ref timings[index];

            // A present replaced before it was shown reports no time; the next shown frame covers its refreshes.
            if (!timing.reportComplete || timing.presentStageCount == 0 || stages[index].time == 0)
                continue;

            frames[written++] = new ShownFrame(timing.presentId, (long)stages[index].time);
        }

        return written;
    }

    internal bool WasAcquired => _acquired;

    /// <summary>What the acquire of the current image signals. The frame's submit waits on it before drawing.</summary>
    internal VkSemaphore AcquireSemaphore => _acquireSemaphore;

    /// <summary>The semaphore this image's present waits on. Signalled by the frame that rendered into it.</summary>
    internal VkSemaphore RenderFinishedSemaphore => _acquired ? _renderFinished[_imageIndex] : VkSemaphore.Null;

    internal int SyncInterval => _presentSyncInterval;

    private void AcquireIfNeeded()
    {
        if (_acquired)
            return;

        if (_needsRecreation)
        {
            _backend.Api.vkDeviceWaitIdle();
            DestroyImagesAndSwapchain();
            Create(_requestedWidth > 0 ? _requestedWidth : Description.Width,
                   _requestedHeight > 0 ? _requestedHeight : Description.Height);
            _requestedWidth = 0;
            _requestedHeight = 0;
        }

        if (_swapchain.IsNull)
            return;

        uint imageIndex = 0;
        var semaphore = _imageAvailable[_backend.CurrentFrameIndex];
        var result = _backend.Api.vkAcquireNextImageKHR(_swapchain, ulong.MaxValue, semaphore, VkFence.Null, &imageIndex);

        if (result is VkResult.ErrorOutOfDateKHR)
        {
            _needsRecreation = true;
            return;
        }

        _imageIndex = (int)imageIndex;
        _acquireSemaphore = semaphore;
        _acquired = true;
        _backend.RegisterAcquiredSwapchain(this);
    }

    private void Create(int width, int height)
    {
        var api = _backend.Api;
        var instanceApi = _backend.InstanceApi;

        instanceApi.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(_backend.PhysicalDevice, _surface, out var capabilities).CheckResult();

        var extent = capabilities.currentExtent.width == uint.MaxValue
                         ? new VkExtent2D((uint)Math.Clamp(width, (int)capabilities.minImageExtent.width, (int)capabilities.maxImageExtent.width),
                                          (uint)Math.Clamp(height, (int)capabilities.minImageExtent.height, (int)capabilities.maxImageExtent.height))
                         : capabilities.currentExtent;

        var format = PickFormat(Description.Format, out var colorSpace);
        _presentMode = PickPresentMode();

        var imageCount = (uint)Math.Max(capabilities.minImageCount, Math.Max(2, Description.BufferCount));

        if (capabilities.maxImageCount > 0)
            imageCount = Math.Min(imageCount, capabilities.maxImageCount);

        VkSwapchainCreateInfoKHR createInfo = new()
                                                  {
                                                      surface = _surface,
                                                      minImageCount = imageCount,
                                                      imageFormat = format,
                                                      imageColorSpace = colorSpace,
                                                      imageExtent = extent,
                                                      imageArrayLayers = 1,

                                                      // Rendered into directly, and copied from for screenshots.
                                                      imageUsage = VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferSrc
                                                                   | VkImageUsageFlags.TransferDst,
                                                      imageSharingMode = VkSharingMode.Exclusive,
                                                      preTransform = capabilities.currentTransform,
                                                      compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
                                                      presentMode = _presentMode,
                                                      clipped = true,
                                                      oldSwapchain = VkSwapchainKHR.Null,
                                                  };

        // Only Fifo paces frames to refreshes, so only there do reports say anything about dropped ones. With
        // present timing on every mode, RADV on Wayland hung in vkQueuePresentKHR after vsync was turned off and on.
        _shownStage = _backend.SupportsPresentTiming && _presentMode == VkPresentModeKHR.Fifo ? QueryShownStage() : VkPresentStageFlagsEXT.None;
        if (_shownStage != VkPresentStageFlagsEXT.None)
            createInfo.flags |= VkSwapchainCreateFlagsKHR.PresentTimingEXT | VkSwapchainCreateFlagsKHR.PresentId2;

        api.vkCreateSwapchainKHR(&createInfo, null, out _swapchain).CheckResult();
        _reportsShownFrames = _shownStage != VkPresentStageFlagsEXT.None && TrySetUpPresentTiming();

        uint actualCount = 0;
        api.vkGetSwapchainImagesKHR(_swapchain, &actualCount, null).CheckResult();
        var images = new VkImage[actualCount];

        fixed (VkImage* pointer = images)
        {
            api.vkGetSwapchainImagesKHR(_swapchain, &actualCount, pointer).CheckResult();
        }

        Description = Description with { Width = (int)extent.width, Height = (int)extent.height, Format = FromVulkan(format) };

        var imageDescription = new TextureDescription
                                   {
                                       Dimension = TextureDimension.Texture2D,
                                       Width = (int)extent.width,
                                       Height = (int)extent.height,
                                       Depth = 1,
                                       ArraySize = 1,
                                       MipLevels = 1,
                                       Format = Description.Format,
                                       Samples = new SampleDescription(1, 0),
                                       Usage = TextureUsage.RenderTarget | TextureUsage.CopySource | TextureUsage.CopyDestination,
                                       Memory = MemoryKind.DeviceLocal,
                                   };

        _images = new VulkanTexture[actualCount];
        _renderFinished = new VkSemaphore[actualCount];

        for (var i = 0; i < actualCount; i++)
        {
            // One per image, not per frame: a present's wait semaphore stays in use until that image is
            // acquired again, which can be several frames later.
            VkSemaphoreCreateInfo semaphoreInfo = new();
            api.vkCreateSemaphore(&semaphoreInfo, null, out _renderFinished[i]).CheckResult();

            // The swapchain owns these images; the wrapper must not free them.
            _images[i] = new VulkanTexture(_backend, images[i], VkDeviceMemory.Null, imageDescription, $"{Label} back buffer {i}")
                             { OwnsImage = false };
        }

        _needsRecreation = false;
        _acquired = false;

        // Present ids count per swapchain object.
        _lastPresentId = 0;
    }

    /// <summary>
    /// The earliest stage this surface can report that marks a frame reaching the display: its first pixel going
    /// out, or failing that, becoming visible. None if the surface reports no presentation timing, or can't take the
    /// present ids the reports are matched by.
    /// </summary>
    private VkPresentStageFlagsEXT QueryShownStage()
    {
        VkSurfaceCapabilitiesPresentId2KHR presentId2Capabilities = new();
        VkPresentTimingSurfaceCapabilitiesEXT timingCapabilities = new() { pNext = &presentId2Capabilities };
        VkSurfaceCapabilities2KHR capabilities = new() { pNext = &timingCapabilities };
        VkPhysicalDeviceSurfaceInfo2KHR surfaceInfo = new() { surface = _surface };
        var result = _backend.InstanceApi.vkGetPhysicalDeviceSurfaceCapabilities2KHR(_backend.PhysicalDevice, &surfaceInfo, &capabilities);
        if (result != VkResult.Success || !timingCapabilities.presentTimingSupported || !presentId2Capabilities.presentId2Supported)
            return VkPresentStageFlagsEXT.None;

        if ((timingCapabilities.presentStageQueries & VkPresentStageFlagsEXT.ImageFirstPixelOut) != 0)
            return VkPresentStageFlagsEXT.ImageFirstPixelOut;

        return (timingCapabilities.presentStageQueries & VkPresentStageFlagsEXT.ImageFirstPixelVisible) != 0
                   ? VkPresentStageFlagsEXT.ImageFirstPixelVisible
                   : VkPresentStageFlagsEXT.None;
    }

    /// <summary>Sizes the swapchain's report queue and picks the clock reports are given in. False if either fails.</summary>
    private bool TrySetUpPresentTiming()
    {
        var api = _backend.Api;
        if (api.vkSetSwapchainPresentTimingQueueSizeEXT(_swapchain, PresentTimingQueueSize) != VkResult.Success)
            return false;

        ulong counter = 0;
        VkSwapchainTimeDomainPropertiesEXT domains = new();
        if (api.vkGetSwapchainTimeDomainPropertiesEXT(_swapchain, &domains, &counter) is not (VkResult.Success or VkResult.Incomplete)
            || domains.timeDomainCount == 0)
            return false;

        // Only differences between reports are used, so any one clock will do.
        var count = domains.timeDomainCount;
        var timeDomains = stackalloc VkTimeDomainKHR[(int)count];
        var timeDomainIds = stackalloc ulong[(int)count];
        domains.pTimeDomains = timeDomains;
        domains.pTimeDomainIds = timeDomainIds;
        if (api.vkGetSwapchainTimeDomainPropertiesEXT(_swapchain, &domains, &counter) is not (VkResult.Success or VkResult.Incomplete))
            return false;

        _timingDomainId = timeDomainIds[0];
        GraphicsLog.Debug?.Invoke($"Present timing: reporting {_shownStage} in {timeDomains[0]} for '{Label}'.");
        return true;
    }

    private VkFormat PickFormat(Format requested, out VkColorSpaceKHR colorSpace)
    {
        uint count = 0;
        _backend.InstanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(_backend.PhysicalDevice, _surface, &count, null).CheckResult();
        var formats = new VkSurfaceFormatKHR[count];

        fixed (VkSurfaceFormatKHR* pointer = formats)
        {
            _backend.InstanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(_backend.PhysicalDevice, _surface, &count, pointer).CheckResult();
        }

        var wanted = VulkanConvert.ToVulkan(requested);

        foreach (var candidate in formats)
        {
            if (candidate.format == wanted)
            {
                colorSpace = candidate.colorSpace;
                return candidate.format;
            }
        }

        if (formats.Length > 0)
        {
            var fallback = PickFallbackFormat(formats, wanted);
            GraphicsLog.WarnOnce($"The window cannot present {requested}; using {fallback.format} instead.");
            colorSpace = fallback.colorSpace;
            return fallback.format;
        }

        colorSpace = VkColorSpaceKHR.SrgbNonLinear;
        return VkFormat.B8G8R8A8Unorm;
    }

    /// <summary>
    /// Keeps the requested encoding when the exact format is missing. X11 surfaces only offer BGRA and list
    /// B8G8R8A8_SRGB first: taking that for a UNorm request makes the hardware gamma-encode colors the shaders
    /// already wrote gamma-encoded, and the whole UI comes out washed out.
    /// </summary>
    private static VkSurfaceFormatKHR PickFallbackFormat(VkSurfaceFormatKHR[] formats, VkFormat wanted)
    {
        var swapped = wanted switch
                          {
                              VkFormat.R8G8B8A8Unorm => VkFormat.B8G8R8A8Unorm,
                              VkFormat.B8G8R8A8Unorm => VkFormat.R8G8B8A8Unorm,
                              VkFormat.R8G8B8A8Srgb  => VkFormat.B8G8R8A8Srgb,
                              VkFormat.B8G8R8A8Srgb  => VkFormat.R8G8B8A8Srgb,
                              _                      => VkFormat.Undefined,
                          };

        foreach (var candidate in formats)
        {
            if (candidate.format == swapped)
                return candidate;
        }

        var wantsSrgb = IsSrgb(wanted);
        foreach (var candidate in formats)
        {
            if (IsSrgb(candidate.format) == wantsSrgb)
                return candidate;
        }

        return formats[0];
    }

    private static bool IsSrgb(VkFormat format)
        => format is VkFormat.B8G8R8A8Srgb or VkFormat.R8G8B8A8Srgb or VkFormat.A8B8G8R8SrgbPack32;

    private VkPresentModeKHR PickPresentMode()
    {
        var hasMailbox = Array.IndexOf(_supportedPresentModes, VkPresentModeKHR.Mailbox) >= 0;

        // Fifo is the only mode every driver has, and it is what a sync interval of 1 means. A second Fifo chain
        // would make the loop wait for two vblanks that never line up, about 1.5 refreshes per frame.
        if (_presentSyncInterval > 0)
            return Description.IsPacedByOtherSwapchain && hasMailbox ? VkPresentModeKHR.Mailbox : VkPresentModeKHR.Fifo;

        // Mailbox before Immediate: both present without waiting, but Mailbox does not tear.
        if (hasMailbox)
            return VkPresentModeKHR.Mailbox;

        return Array.IndexOf(_supportedPresentModes, VkPresentModeKHR.Immediate) >= 0 ? VkPresentModeKHR.Immediate : VkPresentModeKHR.Fifo;
    }

    /// <summary>Queried once: <see cref="PickPresentMode"/> runs every present, and a surface's modes don't change.</summary>
    private VkPresentModeKHR[] QuerySupportedPresentModes()
    {
        uint count = 0;
        _backend.InstanceApi.vkGetPhysicalDeviceSurfacePresentModesKHR(_backend.PhysicalDevice, _surface, &count, null).CheckResult();
        var modes = new VkPresentModeKHR[count];

        fixed (VkPresentModeKHR* pointer = modes)
        {
            _backend.InstanceApi.vkGetPhysicalDeviceSurfacePresentModesKHR(_backend.PhysicalDevice, _surface, &count, pointer).CheckResult();
        }

        return modes;
    }

    private static Format FromVulkan(VkFormat format)
    {
        return format switch
                   {
                       VkFormat.B8G8R8A8Unorm    => Format.B8G8R8A8_UNorm,
                       VkFormat.B8G8R8A8Srgb     => Format.B8G8R8A8_UNorm_SRgb,
                       VkFormat.R8G8B8A8Unorm    => Format.R8G8B8A8_UNorm,
                       VkFormat.R8G8B8A8Srgb     => Format.R8G8B8A8_UNorm_SRgb,
                       VkFormat.A2B10G10R10UnormPack32 => Format.R10G10B10A2_UNorm,
                       VkFormat.R16G16B16A16Sfloat => Format.R16G16B16A16_Float,
                       _                         => Format.B8G8R8A8_UNorm,
                   };
    }

    private void DestroyImagesAndSwapchain()
    {
        foreach (var image in _images)
        {
            image.Dispose();
        }

        foreach (var semaphore in _renderFinished)
        {
            _backend.Api.vkDestroySemaphore(semaphore);
        }

        _images = [];
        _renderFinished = [];

        if (!_swapchain.IsNull)
        {
            _backend.Api.vkDestroySwapchainKHR(_swapchain);
            _swapchain = VkSwapchainKHR.Null;
        }
    }

    protected override void ReleaseWhenRetired()
    {
        _backend.Untrack(this);
        _backend.Api.vkDeviceWaitIdle();
        DestroyImagesAndSwapchain();

        foreach (var semaphore in _imageAvailable)
        {
            _backend.Api.vkDestroySemaphore(semaphore);
        }

        var surface = _surface;
        var instanceApi = _backend.InstanceApi;
        instanceApi.vkDestroySurfaceKHR(surface);
    }

    private readonly VulkanBackend _backend;
    private readonly VkSurfaceKHR _surface;
    private VkSwapchainKHR _swapchain;
    private VulkanTexture[] _images = [];
    private VkSemaphore[] _renderFinished = [];
    private readonly VkSemaphore[] _imageAvailable = new VkSemaphore[VulkanBackend.FramesInFlight];
    private VkSemaphore _acquireSemaphore;
    private int _imageIndex;
    private int _presentSyncInterval = 1;
    private VkPresentModeKHR _presentMode = VkPresentModeKHR.Fifo;
    private readonly VkPresentModeKHR[] _supportedPresentModes;
    private bool _acquired;
    private bool _needsRecreation;

    /** Id of the latest present queued on the current swapchain object; 0 before the first. */
    private ulong _lastPresentId;

    private const ulong PresentWaitTimeoutNs = 1_000_000_000;

    /** Reports are read every frame, so a handful of frames' worth covers the delay until the display reports. */
    private const int PresentTimingQueueSize = 16;

    /** The stage reported for each present, or None when this swapchain doesn't report presentation timing. */
    private VkPresentStageFlagsEXT _shownStage;
    private ulong _timingDomainId;
    private bool _reportsShownFrames;

    /** A resize that arrived while an image was in flight, applied at the next acquire. Zero when none. */
    private int _requestedWidth;
    private int _requestedHeight;
}

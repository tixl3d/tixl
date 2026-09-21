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
    /// Nothing to wait for: <see cref="VulkanBackend.BeginFrame"/> already waits on the fence of the frame
    /// slot it is about to reuse, which is the same bound on how far ahead the CPU may run.
    /// </summary>
    public override void WaitForFrameLatency()
    {
    }

    /// <summary>Presents the acquired image. Called by the backend once the frame's work has been submitted.</summary>
    internal VkResult PresentNow(VkQueue queue, VkSemaphore waitSemaphore)
    {
        var swapchain = _swapchain;
        var imageIndex = (uint)_imageIndex;
        var semaphore = waitSemaphore;

        VkPresentInfoKHR presentInfo = new()
                                           {
                                               waitSemaphoreCount = semaphore.IsNull ? 0u : 1u,
                                               pWaitSemaphores = semaphore.IsNull ? null : &semaphore,
                                               swapchainCount = 1,
                                               pSwapchains = &swapchain,
                                               pImageIndices = &imageIndex,
                                           };

        var result = _backend.Api.vkQueuePresentKHR(queue, &presentInfo);
        _acquired = false;

        // A window that changed size mid-frame is not an error; the next frame renders at the new size.
        if (result is VkResult.ErrorOutOfDateKHR or VkResult.SuboptimalKHR)
            _needsRecreation = true;

        return result;
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

        api.vkCreateSwapchainKHR(&createInfo, null, out _swapchain).CheckResult();

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
            GraphicsLog.WarnOnce($"The window cannot present {requested}; using {formats[0].format} instead.");
            colorSpace = formats[0].colorSpace;
            return formats[0].format;
        }

        colorSpace = VkColorSpaceKHR.SrgbNonLinear;
        return VkFormat.B8G8R8A8Unorm;
    }

    private VkPresentModeKHR PickPresentMode()
    {
        // Fifo is the only mode every driver has, and it is what a sync interval of 1 means.
        if (_presentSyncInterval > 0)
            return VkPresentModeKHR.Fifo;

        uint count = 0;
        _backend.InstanceApi.vkGetPhysicalDeviceSurfacePresentModesKHR(_backend.PhysicalDevice, _surface, &count, null).CheckResult();
        var modes = new VkPresentModeKHR[count];

        fixed (VkPresentModeKHR* pointer = modes)
        {
            _backend.InstanceApi.vkGetPhysicalDeviceSurfacePresentModesKHR(_backend.PhysicalDevice, _surface, &count, pointer).CheckResult();
        }

        // Mailbox before Immediate: both present without waiting, but Mailbox does not tear.
        if (Array.IndexOf(modes, VkPresentModeKHR.Mailbox) >= 0)
            return VkPresentModeKHR.Mailbox;

        return Array.IndexOf(modes, VkPresentModeKHR.Immediate) >= 0 ? VkPresentModeKHR.Immediate : VkPresentModeKHR.Fifo;
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
    private bool _acquired;
    private bool _needsRecreation;

    /** A resize that arrived while an image was in flight, applied at the next acquire. Zero when none. */
    private int _requestedWidth;
    private int _requestedHeight;
}

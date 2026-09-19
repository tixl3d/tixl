using SDL;
using T3.Core.Logging;
using Vortice.Vulkan;
using static SDL.SDL3;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// FIFO swapchain for one SDL window. Owns a "render finished" semaphore per image, because a present may
/// still be reading a semaphore when the next frame in flight would otherwise reuse it.
/// </summary>
internal sealed unsafe class Swapchain : IDisposable
{
    public Swapchain(VulkanDevice device, SDL_Window* window)
    {
        _device = device;
        _window = window;
        Format = ChooseSurfaceFormat();
        Recreate();
    }

    public readonly VkSurfaceFormatKHR Format;
    public VkSwapchainKHR Handle { get; private set; }
    public VkExtent2D Extent { get; private set; }
    public VkImage[] Images { get; private set; } = [];
    public VkImageView[] ImageViews { get; private set; } = [];
    public VkSemaphore[] RenderFinishedSemaphores { get; private set; } = [];

    /// <summary>False while the window has no drawable area (minimized); try again next frame.</summary>
    public bool Recreate()
    {
        var api = _device.DeviceApi;
        api.vkDeviceWaitIdle();

        _device.InstanceApi.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(_device.PhysicalDevice, _device.Surface, out var capabilities).CheckResult();
        var extent = ChooseExtent(capabilities);
        if (extent.width == 0 || extent.height == 0)
            return false;

        var imageCount = Math.Max(capabilities.minImageCount, 3);
        if (capabilities.maxImageCount > 0)
            imageCount = Math.Min(imageCount, capabilities.maxImageCount);

        var oldSwapchain = Handle;
        VkSwapchainCreateInfoKHR createInfo = new()
                                                  {
                                                      surface = _device.Surface,
                                                      minImageCount = imageCount,
                                                      imageFormat = Format.format,
                                                      imageColorSpace = Format.colorSpace,
                                                      imageExtent = extent,
                                                      imageArrayLayers = 1,
                                                      imageUsage = VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferDst,
                                                      imageSharingMode = VkSharingMode.Exclusive,
                                                      preTransform = capabilities.currentTransform,
                                                      compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
                                                      presentMode = VkPresentModeKHR.Fifo,
                                                      clipped = true,
                                                      oldSwapchain = oldSwapchain,
                                                  };
        api.vkCreateSwapchainKHR(&createInfo, null, out var newSwapchain).CheckResult();

        DestroyImageResources();
        if (oldSwapchain != VkSwapchainKHR.Null)
            api.vkDestroySwapchainKHR(oldSwapchain);

        Handle = newSwapchain;
        Extent = extent;

        uint actualImageCount = 0;
        api.vkGetSwapchainImagesKHR(Handle, &actualImageCount, null).CheckResult();
        Images = new VkImage[actualImageCount];
        api.vkGetSwapchainImagesKHR(Handle, Images).CheckResult();

        ImageViews = new VkImageView[actualImageCount];
        RenderFinishedSemaphores = new VkSemaphore[actualImageCount];
        for (var index = 0; index < actualImageCount; index++)
        {
            VkImageViewCreateInfo viewInfo = new(Images[index],
                                                 VkImageViewType.Image2D,
                                                 Format.format,
                                                 VkComponentMapping.Rgba,
                                                 new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1));
            api.vkCreateImageView(&viewInfo, null, out ImageViews[index]).CheckResult();
            api.vkCreateSemaphore(out RenderFinishedSemaphores[index]).CheckResult();
        }

        Log.Info($"Swapchain {extent.width}x{extent.height}, {actualImageCount} images, {Format.format}");
        return true;
    }

    public void Dispose()
    {
        DestroyImageResources();
        if (Handle != VkSwapchainKHR.Null)
            _device.DeviceApi.vkDestroySwapchainKHR(Handle);
    }

    private VkSurfaceFormatKHR ChooseSurfaceFormat()
    {
        uint formatCount = 0;
        _device.InstanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(_device.PhysicalDevice, _device.Surface, &formatCount, null).CheckResult();
        var formats = new VkSurfaceFormatKHR[formatCount];
        _device.InstanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(_device.PhysicalDevice, _device.Surface, formats).CheckResult();

        // TiXL's D3D11 swapchain is non-sRGB 8-bit; shaders write display values directly.
        foreach (var format in formats)
        {
            if (format.format is VkFormat.B8G8R8A8Unorm or VkFormat.R8G8B8A8Unorm
                && format.colorSpace == VkColorSpaceKHR.SrgbNonLinear)
                return format;
        }

        return formats[0];
    }

    private VkExtent2D ChooseExtent(VkSurfaceCapabilitiesKHR capabilities)
    {
        // Wayland leaves the extent to the application and reports 0xFFFFFFFF.
        if (capabilities.currentExtent.width != uint.MaxValue)
            return capabilities.currentExtent;

        int width, height;
        SDL_GetWindowSizeInPixels(_window, &width, &height);
        return new VkExtent2D(Math.Clamp((uint)width, capabilities.minImageExtent.width, capabilities.maxImageExtent.width),
                              Math.Clamp((uint)height, capabilities.minImageExtent.height, capabilities.maxImageExtent.height));
    }

    private void DestroyImageResources()
    {
        foreach (var view in ImageViews)
        {
            _device.DeviceApi.vkDestroyImageView(view);
        }

        foreach (var semaphore in RenderFinishedSemaphores)
        {
            _device.DeviceApi.vkDestroySemaphore(semaphore);
        }

        ImageViews = [];
        RenderFinishedSemaphores = [];
    }

    private readonly VulkanDevice _device;
    private readonly SDL_Window* _window;
}

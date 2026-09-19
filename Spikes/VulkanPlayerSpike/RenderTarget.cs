using Vortice.Vulkan;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// An off-screen colour target that a later pass samples — what [RenderTarget] plus [SrvFromTexture2d] give an
/// image-effect chain. Tracks its own layout so the caller only says what it wants to do next.
/// </summary>
internal sealed unsafe class RenderTarget : IDisposable
{
    public RenderTarget(VulkanDevice device, VkExtent2D extent, VkFormat format)
    {
        _device = device;
        Extent = extent;
        Format = format;

        VkImageCreateInfo imageInfo = new()
                                          {
                                              imageType = VkImageType.Image2D,
                                              format = format,
                                              extent = new VkExtent3D(extent.width, extent.height, 1),
                                              mipLevels = 1,
                                              arrayLayers = 1,
                                              samples = VkSampleCountFlags.Count1,
                                              tiling = VkImageTiling.Optimal,
                                              // TransferSrc so a target can be read back (captures, and later the visual tests).
                                              usage = VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled
                                                      | VkImageUsageFlags.TransferSrc,
                                              initialLayout = VkImageLayout.Undefined,
                                          };
        device.DeviceApi.vkCreateImage(&imageInfo, null, out Image).CheckResult();

        device.DeviceApi.vkGetImageMemoryRequirements(Image, out var requirements);
        VkMemoryAllocateInfo allocateInfo = new()
                                                {
                                                    allocationSize = requirements.size,
                                                    memoryTypeIndex = device.FindMemoryType(requirements.memoryTypeBits,
                                                                                            VkMemoryPropertyFlags.DeviceLocal),
                                                };
        device.DeviceApi.vkAllocateMemory(&allocateInfo, null, out _memory).CheckResult();
        device.DeviceApi.vkBindImageMemory(Image, _memory, 0).CheckResult();

        VkImageViewCreateInfo viewInfo = new(Image,
                                             VkImageViewType.Image2D,
                                             format,
                                             VkComponentMapping.Rgba,
                                             new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1));
        device.DeviceApi.vkCreateImageView(&viewInfo, null, out View).CheckResult();
    }

    public readonly VkImage Image;
    public readonly VkImageView View;
    public readonly VkExtent2D Extent;
    public readonly VkFormat Format;

    /// <summary>Barriers to render into this target. The first use of a frame discards the old contents.</summary>
    public void TransitionToColorAttachment(VkCommandBuffer commandBuffer)
    {
        _device.TransitionImage(commandBuffer, Image,
                                _layout, VkImageLayout.ColorAttachmentOptimal,
                                VkPipelineStageFlags2.FragmentShader, VkAccessFlags2.ShaderSampledRead,
                                VkPipelineStageFlags2.ColorAttachmentOutput, VkAccessFlags2.ColorAttachmentWrite);
        _layout = VkImageLayout.ColorAttachmentOptimal;
    }

    /// <summary>Barriers so the next pass can sample what was just rendered.</summary>
    public void TransitionToShaderRead(VkCommandBuffer commandBuffer)
    {
        _device.TransitionImage(commandBuffer, Image,
                                _layout, VkImageLayout.ShaderReadOnlyOptimal,
                                VkPipelineStageFlags2.ColorAttachmentOutput, VkAccessFlags2.ColorAttachmentWrite,
                                VkPipelineStageFlags2.FragmentShader, VkAccessFlags2.ShaderSampledRead);
        _layout = VkImageLayout.ShaderReadOnlyOptimal;
    }

    public void Dispose()
    {
        _device.DeviceApi.vkDestroyImageView(View);
        _device.DeviceApi.vkDestroyImage(Image);
        _device.DeviceApi.vkFreeMemory(_memory);
    }

    private readonly VulkanDevice _device;
    private readonly VkDeviceMemory _memory;
    private VkImageLayout _layout = VkImageLayout.Undefined;
}

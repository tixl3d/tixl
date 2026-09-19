using Vortice.Vulkan;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// A 256×1 colour ramp uploaded through a staging buffer, standing in for the gradient texture
/// [MandelbrotFractal] samples. A periodic cosine palette, so wrap addressing cycles smoothly.
/// </summary>
internal sealed unsafe class GradientTexture : IDisposable
{
    public GradientTexture(VulkanDevice device)
    {
        _device = device;
        var api = device.DeviceApi;
        const int width = 256;

        VkBufferCreateInfo stagingInfo = new() { size = width * 4, usage = VkBufferUsageFlags.TransferSrc };
        api.vkCreateBuffer(&stagingInfo, null, out var stagingBuffer).CheckResult();
        api.vkGetBufferMemoryRequirements(stagingBuffer, out var stagingRequirements);
        VkMemoryAllocateInfo stagingAllocate = new()
                                                   {
                                                       allocationSize = stagingRequirements.size,
                                                       memoryTypeIndex = device.FindMemoryType(stagingRequirements.memoryTypeBits,
                                                                                               VkMemoryPropertyFlags.HostVisible
                                                                                               | VkMemoryPropertyFlags.HostCoherent),
                                                   };
        api.vkAllocateMemory(&stagingAllocate, null, out var stagingMemory).CheckResult();
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
        api.vkCreateImage(&imageInfo, null, out _image).CheckResult();
        api.vkGetImageMemoryRequirements(_image, out var requirements);
        VkMemoryAllocateInfo allocateInfo = new()
                                                {
                                                    allocationSize = requirements.size,
                                                    memoryTypeIndex = device.FindMemoryType(requirements.memoryTypeBits,
                                                                                            VkMemoryPropertyFlags.DeviceLocal),
                                                };
        api.vkAllocateMemory(&allocateInfo, null, out _memory).CheckResult();
        api.vkBindImageMemory(_image, _memory, 0).CheckResult();

        var image = _image;
        device.SubmitAndWait(commandBuffer =>
                             {
                                 device.TransitionImage(commandBuffer, image,
                                                        VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal,
                                                        VkPipelineStageFlags2.None, VkAccessFlags2.None,
                                                        VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite);
                                 VkBufferImageCopy region = new()
                                                                {
                                                                    imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                                                                    imageExtent = new VkExtent3D(width, 1, 1),
                                                                };
                                 device.DeviceApi.vkCmdCopyBufferToImage(commandBuffer, stagingBuffer, image,
                                                                         VkImageLayout.TransferDstOptimal, 1, &region);
                                 device.TransitionImage(commandBuffer, image,
                                                        VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
                                                        VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferWrite,
                                                        VkPipelineStageFlags2.FragmentShader, VkAccessFlags2.ShaderSampledRead);
                             });

        api.vkDestroyBuffer(stagingBuffer);
        api.vkFreeMemory(stagingMemory);

        VkImageViewCreateInfo viewInfo = new(_image,
                                             VkImageViewType.Image2D,
                                             VkFormat.R8G8B8A8Unorm,
                                             VkComponentMapping.Rgba,
                                             new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1));
        api.vkCreateImageView(&viewInfo, null, out View).CheckResult();
    }

    public readonly VkImageView View;

    public void Dispose()
    {
        _device.DeviceApi.vkDestroyImageView(View);
        _device.DeviceApi.vkDestroyImage(_image);
        _device.DeviceApi.vkFreeMemory(_memory);
    }

    private static byte PaletteChannel(float t, float phase)
    {
        var value = 0.5f + 0.5f * MathF.Cos(MathF.Tau * (t + phase));
        return (byte)(value * 255);
    }

    private readonly VulkanDevice _device;
    private readonly VkImage _image;
    private readonly VkDeviceMemory _memory;
}

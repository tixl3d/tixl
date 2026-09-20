using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace T3.Graphics.Vulkan;

/// <summary>
/// D3D11 tracks hazards itself: bind a texture that was just rendered into and it works. Vulkan does not, so
/// every use is preceded by the barrier that use needs. The backend keeps the last layout and access of each
/// resource and emits a barrier only when they actually change.
/// </summary>
internal static unsafe class VulkanBarriers
{
    internal static void TransitionImage(VulkanBackend backend, VkCommandBuffer commandBuffer, VulkanTexture texture,
                                         VkImageLayout layout, VkPipelineStageFlags2 stage, VkAccessFlags2 access)
    {
        if (texture.Image.IsNull)
            return;

        var isWrite = (access & WriteAccess) != 0;

        // A read after a read in the same layout needs nothing; anything else does.
        if (texture.Layout == layout && !isWrite && (texture.LastAccess & WriteAccess) == 0)
            return;

        VkImageMemoryBarrier2 barrier = new()
                                            {
                                                srcStageMask = texture.LastStage,
                                                srcAccessMask = texture.LastAccess,
                                                dstStageMask = stage,
                                                dstAccessMask = access,
                                                oldLayout = texture.Layout,
                                                newLayout = layout,
                                                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                                                dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                                                image = texture.Image,
                                                subresourceRange = new VkImageSubresourceRange
                                                                       {
                                                                           aspectMask = texture.Aspect,
                                                                           baseMipLevel = 0,
                                                                           levelCount = VK_REMAINING_MIP_LEVELS,
                                                                           baseArrayLayer = 0,
                                                                           layerCount = VK_REMAINING_ARRAY_LAYERS,
                                                                       },
                                            };

        VkDependencyInfo dependency = new()
                                          {
                                              imageMemoryBarrierCount = 1,
                                              pImageMemoryBarriers = &barrier,
                                          };

        backend.Api.vkCmdPipelineBarrier2(commandBuffer, &dependency);

        texture.Layout = layout;
        texture.LastStage = stage;
        texture.LastAccess = access;
    }

    internal static void BarrierBuffer(VulkanBackend backend, VkCommandBuffer commandBuffer, VulkanBuffer buffer,
                                       VkPipelineStageFlags2 stage, VkAccessFlags2 access)
    {
        if (buffer.Buffer.IsNull)
            return;

        var isWrite = (access & WriteAccess) != 0;

        if (!isWrite && (buffer.LastAccess & WriteAccess) == 0)
        {
            buffer.LastStage |= stage;
            buffer.LastAccess |= access;
            return;
        }

        VkBufferMemoryBarrier2 barrier = new()
                                             {
                                                 srcStageMask = buffer.LastStage,
                                                 srcAccessMask = buffer.LastAccess,
                                                 dstStageMask = stage,
                                                 dstAccessMask = access,
                                                 srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                                                 dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                                                 buffer = buffer.Buffer,
                                                 offset = 0,
                                                 size = VK_WHOLE_SIZE,
                                             };

        VkDependencyInfo dependency = new()
                                          {
                                              bufferMemoryBarrierCount = 1,
                                              pBufferMemoryBarriers = &barrier,
                                          };

        backend.Api.vkCmdPipelineBarrier2(commandBuffer, &dependency);

        buffer.LastStage = stage;
        buffer.LastAccess = access;
    }

    private const VkAccessFlags2 WriteAccess = VkAccessFlags2.ShaderWrite | VkAccessFlags2.ShaderStorageWrite
                                               | VkAccessFlags2.ColorAttachmentWrite | VkAccessFlags2.DepthStencilAttachmentWrite
                                               | VkAccessFlags2.TransferWrite | VkAccessFlags2.HostWrite | VkAccessFlags2.MemoryWrite;
}

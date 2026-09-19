using T3.Core.Logging;
using Vortice.Vulkan;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// Renders the chain once into an off-screen target and copies it back to the CPU — the shape the visual test
/// suite needs on Vulkan, and the only way to check the result without looking at the window.
/// Writes raw RGBA plus a one-line header, which <c>Spikes/VulkanPlayerSpike/raw_to_png.py</c> converts.
/// </summary>
internal static unsafe class FrameCapture
{
    public static void WriteToFile(VulkanDevice device, ImageEffectChain chain, VkExtent2D extent, VkFormat format,
                                   float timeSec, string path)
    {
        // The effect pipeline was built for this format; a dynamic-rendering attachment has to match it.
        chain.EnsureSize(extent);
        using var target = new RenderTarget(device, extent, format);

        var api = device.DeviceApi;
        var sizeInBytes = (ulong)(extent.width * extent.height * 4);
        VkBufferCreateInfo bufferInfo = new() { size = sizeInBytes, usage = VkBufferUsageFlags.TransferDst };
        api.vkCreateBuffer(&bufferInfo, null, out var readbackBuffer).CheckResult();
        api.vkGetBufferMemoryRequirements(readbackBuffer, out var requirements);
        VkMemoryAllocateInfo allocateInfo = new()
                                                {
                                                    allocationSize = requirements.size,
                                                    memoryTypeIndex = device.FindMemoryType(requirements.memoryTypeBits,
                                                                                            VkMemoryPropertyFlags.HostVisible
                                                                                            | VkMemoryPropertyFlags.HostCoherent),
                                                };
        api.vkAllocateMemory(&allocateInfo, null, out var readbackMemory).CheckResult();
        api.vkBindBufferMemory(readbackBuffer, readbackMemory, 0).CheckResult();

        device.SubmitAndWait(commandBuffer =>
                             {
                                 chain.RenderOffscreen(commandBuffer, timeSec);

                                 target.TransitionToColorAttachment(commandBuffer);
                                 VkRenderingAttachmentInfo colorAttachment = new()
                                                                                 {
                                                                                     imageView = target.View,
                                                                                     imageLayout = VkImageLayout.ColorAttachmentOptimal,
                                                                                     loadOp = VkAttachmentLoadOp.DontCare,
                                                                                     storeOp = VkAttachmentStoreOp.Store,
                                                                                 };
                                 VkRenderingInfo renderingInfo = new()
                                                                     {
                                                                         renderArea = new VkRect2D(VkOffset2D.Zero, extent),
                                                                         layerCount = 1,
                                                                         colorAttachmentCount = 1,
                                                                         pColorAttachments = &colorAttachment,
                                                                     };
                                 api.vkCmdBeginRendering(commandBuffer, &renderingInfo);
                                 chain.DrawEffect(commandBuffer, extent, timeSec);
                                 api.vkCmdEndRendering(commandBuffer);

                                 device.TransitionImage(commandBuffer, target.Image,
                                                        VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferSrcOptimal,
                                                        VkPipelineStageFlags2.ColorAttachmentOutput, VkAccessFlags2.ColorAttachmentWrite,
                                                        VkPipelineStageFlags2.Copy, VkAccessFlags2.TransferRead);

                                 VkBufferImageCopy region = new()
                                                                {
                                                                    imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                                                                    imageExtent = new VkExtent3D(extent.width, extent.height, 1),
                                                                };
                                 api.vkCmdCopyImageToBuffer(commandBuffer, target.Image, VkImageLayout.TransferSrcOptimal,
                                                            readbackBuffer, 1, &region);
                             });

        void* mapped;
        api.vkMapMemory(readbackMemory, 0, sizeInBytes, 0, &mapped).CheckResult();
        using (var file = File.Create(path))
        using (var writer = new BinaryWriter(file))
        {
            var channelOrder = format == VkFormat.B8G8R8A8Unorm ? "BGRA" : "RGBA";
            writer.Write(System.Text.Encoding.ASCII.GetBytes($"{channelOrder} {extent.width} {extent.height}\n"));
            writer.Write(new ReadOnlySpan<byte>(mapped, (int)sizeInBytes));
        }

        api.vkUnmapMemory(readbackMemory);
        api.vkDestroyBuffer(readbackBuffer);
        api.vkFreeMemory(readbackMemory);
        Log.Info($"Captured {extent.width}x{extent.height} to {path}");
    }
}

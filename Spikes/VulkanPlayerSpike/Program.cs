using SDL;
using T3.Core.Logging;
using Vortice.Vulkan;
using static SDL.SDL3;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// Throwaway proof that SDL3, Vulkan and Slang work together on TiXL's own shaders before the real Player
/// moves over. Options: <c>--frames N</c> exits after N frames, <c>--no-validation</c> skips the validation layer.
/// </summary>
internal static unsafe class Program
{
    private static int Main(string[] args)
    {
        Log.AddWriter(new ConsoleWriter());

        var maxFrames = ReadIntOption(args, "--frames", 0);
        var enableValidation = !args.Contains("--no-validation");

        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
        {
            Log.Error($"SDL_Init failed: {SDL_GetError()}");
            return 1;
        }

        Log.Info($"SDL video driver: {SDL_GetCurrentVideoDriver()}");
        var window = SDL_CreateWindow("TiXL - Vulkan Player Spike", 1280, 720,
                                      SDL_WindowFlags.SDL_WINDOW_VULKAN | SDL_WindowFlags.SDL_WINDOW_RESIZABLE
                                                                        | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY);
        if (window == null)
        {
            Log.Error($"SDL_CreateWindow failed: {SDL_GetError()}");
            return 1;
        }

        int renderedFrames;
        using (var device = new VulkanDevice(window, enableValidation))
        using (var swapchain = new Swapchain(device, window))
        using (var pass = new MandelbrotPass(device, swapchain.Format.format, FindShaderRoot(), FramesInFlight))
        using (var frames = new FrameResources(device, FramesInFlight))
        {
            renderedFrames = RunLoop(window, device, swapchain, pass, frames, maxFrames);
        }

        SDL_DestroyWindow(window);
        SDL_Quit();

        var errorCount = VulkanDevice.ValidationErrorCount;
        Log.Info($"Rendered {renderedFrames} frames, {errorCount} validation errors");
        return errorCount == 0 ? 0 : 2;
    }

    private static int RunLoop(SDL_Window* window, VulkanDevice device, Swapchain swapchain, MandelbrotPass pass,
                               FrameResources frames, int maxFrames)
    {
        var api = device.DeviceApi;
        var startTicksNs = SDL_GetTicksNS();
        var swapchainIsStale = false;
        var renderedFrames = 0;
        var frameIndex = 0;

        while (maxFrames == 0 || renderedFrames < maxFrames)
        {
            SDL_Event sdlEvent;
            var quitRequested = false;
            while (SDL_PollEvent(&sdlEvent))
            {
                switch (sdlEvent.Type)
                {
                    case SDL_EventType.SDL_EVENT_QUIT:
                    case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                        quitRequested = true;
                        break;
                    case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                        swapchainIsStale = true;
                        break;
                    case SDL_EventType.SDL_EVENT_KEY_DOWN when sdlEvent.key.key == SDL_Keycode.SDLK_ESCAPE:
                        quitRequested = true;
                        break;
                    case SDL_EventType.SDL_EVENT_KEY_DOWN when sdlEvent.key.key == SDL_Keycode.SDLK_F11:
                        var isFullscreen = (SDL_GetWindowFlags(window) & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0;
                        SDL_SetWindowFullscreen(window, !isFullscreen);
                        break;
                }
            }

            if (quitRequested)
                break;

            if (swapchainIsStale)
            {
                if (!swapchain.Recreate())
                {
                    SDL_Delay(16);
                    continue;
                }

                swapchainIsStale = false;
            }

            ref var frame = ref frames[frameIndex];
            api.vkWaitForFences(frame.InFlightFence, true, ulong.MaxValue).CheckResult();

            uint imageIndex;
            var acquireResult = api.vkAcquireNextImageKHR(swapchain.Handle, ulong.MaxValue, frame.ImageAvailable, VkFence.Null, &imageIndex);
            if (acquireResult == VkResult.ErrorOutOfDateKHR)
            {
                swapchainIsStale = true;
                continue;
            }

            if (acquireResult != VkResult.Success && acquireResult != VkResult.SuboptimalKHR)
                throw new InvalidOperationException($"vkAcquireNextImageKHR failed: {acquireResult}");

            api.vkResetFences(frame.InFlightFence);
            api.vkResetCommandPool(frame.CommandPool, VkCommandPoolResetFlags.None);

            var timeSec = (SDL_GetTicksNS() - startTicksNs) / 1e9f;
            RecordFrame(device, swapchain, pass, frame.CommandBuffer, imageIndex, frameIndex, timeSec);

            var commandBuffer = frame.CommandBuffer;
            var waitSemaphore = frame.ImageAvailable;
            var signalSemaphore = swapchain.RenderFinishedSemaphores[imageIndex];
            var waitStage = VkPipelineStageFlags.ColorAttachmentOutput;
            VkSubmitInfo submitInfo = new()
                                          {
                                              waitSemaphoreCount = 1,
                                              pWaitSemaphores = &waitSemaphore,
                                              pWaitDstStageMask = &waitStage,
                                              commandBufferCount = 1,
                                              pCommandBuffers = &commandBuffer,
                                              signalSemaphoreCount = 1,
                                              pSignalSemaphores = &signalSemaphore,
                                          };
            api.vkQueueSubmit(device.Queue, 1, &submitInfo, frame.InFlightFence).CheckResult();

            var swapchainHandle = swapchain.Handle;
            VkPresentInfoKHR presentInfo = new()
                                               {
                                                   waitSemaphoreCount = 1,
                                                   pWaitSemaphores = &signalSemaphore,
                                                   swapchainCount = 1,
                                                   pSwapchains = &swapchainHandle,
                                                   pImageIndices = &imageIndex,
                                               };
            var presentResult = api.vkQueuePresentKHR(device.Queue, &presentInfo);
            if (presentResult is VkResult.ErrorOutOfDateKHR or VkResult.SuboptimalKHR)
            {
                swapchainIsStale = true;
            }
            else if (presentResult != VkResult.Success)
            {
                throw new InvalidOperationException($"vkQueuePresentKHR failed: {presentResult}");
            }

            renderedFrames++;
            frameIndex = (frameIndex + 1) % FramesInFlight;
            if (renderedFrames % 300 == 0)
                Log.Info($"{renderedFrames} frames, {renderedFrames / timeSec:0.0} fps average");
        }

        return renderedFrames;
    }

    private static void RecordFrame(VulkanDevice device, Swapchain swapchain, MandelbrotPass pass,
                                    VkCommandBuffer commandBuffer, uint imageIndex, int frameIndex, float timeSec)
    {
        var api = device.DeviceApi;
        VkCommandBufferBeginInfo beginInfo = new() { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        api.vkBeginCommandBuffer(commandBuffer, &beginInfo).CheckResult();

        var image = swapchain.Images[imageIndex];
        device.TransitionImage(commandBuffer, image,
                               VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal,
                               VkPipelineStageFlags2.ColorAttachmentOutput, VkAccessFlags2.None,
                               VkPipelineStageFlags2.ColorAttachmentOutput, VkAccessFlags2.ColorAttachmentWrite);

        VkRenderingAttachmentInfo colorAttachment = new()
                                                        {
                                                            imageView = swapchain.ImageViews[imageIndex],
                                                            imageLayout = VkImageLayout.ColorAttachmentOptimal,
                                                            loadOp = VkAttachmentLoadOp.DontCare,
                                                            storeOp = VkAttachmentStoreOp.Store,
                                                        };
        VkRenderingInfo renderingInfo = new()
                                            {
                                                renderArea = new VkRect2D(VkOffset2D.Zero, swapchain.Extent),
                                                layerCount = 1,
                                                colorAttachmentCount = 1,
                                                pColorAttachments = &colorAttachment,
                                            };
        api.vkCmdBeginRendering(commandBuffer, &renderingInfo);
        pass.Draw(commandBuffer, frameIndex, swapchain.Extent, timeSec);
        api.vkCmdEndRendering(commandBuffer);

        device.TransitionImage(commandBuffer, image,
                               VkImageLayout.ColorAttachmentOptimal, VkImageLayout.PresentSrcKHR,
                               VkPipelineStageFlags2.ColorAttachmentOutput, VkAccessFlags2.ColorAttachmentWrite,
                               VkPipelineStageFlags2.None, VkAccessFlags2.None);

        api.vkEndCommandBuffer(commandBuffer).CheckResult();
    }

    private static string FindShaderRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "Operators", "Lib", "Assets", "shaders");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("Operators/Lib/Assets/shaders not found above " + AppContext.BaseDirectory);
    }

    private static int ReadIntOption(string[] args, string name, int defaultValue)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : defaultValue;
    }

    private const int FramesInFlight = 2;
}

/// <summary>Per-frame-in-flight command recording and CPU/GPU pacing objects.</summary>
internal sealed unsafe class FrameResources : IDisposable
{
    public FrameResources(VulkanDevice device, int count)
    {
        _device = device;
        _frames = new Frame[count];
        for (var index = 0; index < count; index++)
        {
            VkCommandPoolCreateInfo poolInfo = new()
                                                   {
                                                       flags = VkCommandPoolCreateFlags.Transient,
                                                       queueFamilyIndex = device.QueueFamilyIndex,
                                                   };
            device.DeviceApi.vkCreateCommandPool(&poolInfo, null, out _frames[index].CommandPool).CheckResult();
            device.DeviceApi.vkAllocateCommandBuffer(_frames[index].CommandPool, out _frames[index].CommandBuffer).CheckResult();
            device.DeviceApi.vkCreateFence(VkFenceCreateFlags.Signaled, out _frames[index].InFlightFence).CheckResult();
            device.DeviceApi.vkCreateSemaphore(out _frames[index].ImageAvailable).CheckResult();
        }
    }

    public ref Frame this[int index] => ref _frames[index];

    public void Dispose()
    {
        var api = _device.DeviceApi;
        api.vkDeviceWaitIdle();
        foreach (var frame in _frames)
        {
            api.vkDestroyFence(frame.InFlightFence);
            api.vkDestroySemaphore(frame.ImageAvailable);
            api.vkDestroyCommandPool(frame.CommandPool);
        }
    }

    internal struct Frame
    {
        public VkCommandPool CommandPool;
        public VkCommandBuffer CommandBuffer;
        public VkFence InFlightFence;
        public VkSemaphore ImageAvailable;
    }

    private readonly VulkanDevice _device;
    private readonly Frame[] _frames;
}

namespace T3.Graphics;

/// <summary>
/// The chain of images a window is presented from. Created from a window the platform layer owns, because
/// what a backend needs from it differs: D3D11 wants the Win32 handle, Vulkan wants a surface made against
/// its own instance.
/// </summary>
public abstract class GpuSwapchain(SwapchainDescription description, string? label) : GpuResource(label)
{
    public SwapchainDescription Description { get; protected set; } = description;

    public int Width => Description.Width;
    public int Height => Description.Height;

    /// <summary>
    /// The image this frame renders into. Reading it is what commits the frame to a particular image, so
    /// call it once rendering to the window actually starts.
    /// </summary>
    public abstract GpuTexture BackBuffer { get; }

    /// <summary>Resizes to match the window. The previous back buffers and any views of them become invalid.</summary>
    public abstract void Resize(int width, int height);

    /// <summary>
    /// Shows what was rendered. <paramref name="syncInterval"/> is D3D11's: 0 presents as fast as the driver
    /// allows, 1 waits for one vertical blank.
    /// </summary>
    /// <remarks>
    /// The image reaches the screen by the end of the frame, not necessarily inside this call: Vulkan has to
    /// present after the frame's work is submitted, so it queues the present and <c>EndFrame</c> issues it.
    /// </remarks>
    public abstract void Present(int syncInterval);

    /// <summary>
    /// Blocks until the driver is ready for the next frame, which is how the editor keeps input latency down
    /// instead of letting frames queue up.
    /// </summary>
    public abstract void WaitForFrameLatency();
}

public readonly record struct SwapchainDescription
{
    public int Width { get; init; }
    public int Height { get; init; }
    public Format Format { get; init; }

    /// <summary>Two for the lowest latency, three to keep the GPU busy when a frame runs long.</summary>
    public int BufferCount { get; init; }

    /// <summary>
    /// Lets the editor wait for the driver rather than for a full frame. D3D11 calls this a waitable
    /// swapchain; Vulkan gets it from the frame fences it already has.
    /// </summary>
    public bool WaitableForLatency { get; init; }

    /// <summary>Allows the borderless-fullscreen path to change the display mode, as the Player's does.</summary>
    public bool AllowModeSwitch { get; init; }
}

/// <summary>
/// The window to present into. The platform layer fills in the one its backend needs — it is the only part
/// of TiXL that knows about SDL, and this keeps that knowledge out of the graphics layer.
/// </summary>
public readonly record struct SurfaceTarget
{
    /// <summary>The Win32 window handle, for the D3D11 backend.</summary>
    public nint Win32Window { get; init; }

    /// <summary>
    /// Makes a VkSurfaceKHR for the given VkInstance and returns its handle, for the Vulkan backend. A
    /// callback because creating it is SDL's job while the instance belongs to the backend.
    /// </summary>
    public Func<nint, nint>? CreateVulkanSurface { get; init; }
}

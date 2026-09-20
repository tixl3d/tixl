using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// What the editor and the Player present through. Keeps SharpDX's shape — get a back buffer, make a render
/// target view of it, present, resize — so the window code migrates by swapping a using line.
/// </summary>
public sealed class SwapChain : IDisposable
{
    /// <summary>Null when the window cannot be presented to, which the caller reports rather than crashing on.</summary>
    public static SwapChain? TryCreate(Device device, in SwapChainDescription description, in SurfaceTarget target, string? label = null)
    {
        var swapchain = device.Backend.CreateSwapchain(new SwapchainDescription
                                                           {
                                                               Width = description.Width,
                                                               Height = description.Height,
                                                               Format = description.Format,
                                                               BufferCount = description.BufferCount,
                                                               WaitableForLatency = description.UseFrameLatencyWaitableObject,
                                                               AllowModeSwitch = description.AllowModeSwitch,
                                                           },
                                                       target, label);

        return swapchain == null ? null : new SwapChain(device, swapchain);
    }

    private SwapChain(Device device, GpuSwapchain swapchain)
    {
        _device = device;
        _swapchain = swapchain;
    }

    public int Width => _swapchain.Width;
    public int Height => _swapchain.Height;
    public Format Format => _swapchain.Description.Format;

    /// <summary>
    /// The texture this frame renders into. Under Vulkan asking for it is what picks the image, so ask once
    /// per frame and not before the frame starts.
    /// </summary>
    public Texture2D GetBackBuffer()
    {
        var texture = _swapchain.BackBuffer;

        // One wrapper per underlying image, reused: a swapchain hands out the same few images forever, and
        // views of them are cached by the caller against the wrapper's identity.
        if (_backBuffers.TryGetValue(texture, out var wrapper) && !wrapper.IsDisposed)
            return wrapper;

        wrapper = new Texture2D(_device, texture,
                                new Texture2DDescription
                                    {
                                        Width = texture.Description.Width,
                                        Height = texture.Description.Height,
                                        MipLevels = 1,
                                        ArraySize = 1,
                                        Format = texture.Description.Format,
                                        SampleDescription = new SampleDescription(1, 0),
                                        BindFlags = BindFlags.RenderTarget,
                                        Usage = ResourceUsage.Default,
                                    });

        _backBuffers[texture] = wrapper;
        return wrapper;
    }

    /// <summary>
    /// Resizes to match the window. Every view of a back buffer is invalid afterwards, so the render loop
    /// recreates them rather than keeping them across a resize.
    /// </summary>
    public void ResizeBuffers(int width, int height)
    {
        _backBuffers.Clear();
        _swapchain.Resize(width, height);
    }

    /// <summary>0 presents as fast as the driver allows, 1 waits for one vertical blank.</summary>
    public void Present(int syncInterval) => _swapchain.Present(syncInterval);

    public void WaitForFrameLatency() => _swapchain.WaitForFrameLatency();

    public void Dispose()
    {
        _backBuffers.Clear();
        _swapchain.Dispose();
    }

    private readonly Device _device;
    private readonly GpuSwapchain _swapchain;
    private readonly Dictionary<GpuTexture, Texture2D> _backBuffers = [];
}

/// <summary>Only the fields TiXL sets; everything else D3D11 offers stayed at its default anyway.</summary>
public struct SwapChainDescription
{
    public int Width;
    public int Height;
    public Format Format;
    public int BufferCount;

    /// <summary>The editor waits on this once a frame to keep input latency down.</summary>
    public bool UseFrameLatencyWaitableObject;

    public bool AllowModeSwitch;
}

using T3.Graphics;
using DXGI = SharpDX.DXGI;
using D3D11Device = SharpDX.Direct3D11.Device;

namespace T3.Graphics.D3D11;

/// <summary>
/// A DXGI flip-model swapchain. Present is one call here, so the backend API's "present by the end of the
/// frame" is satisfied immediately — unlike Vulkan, which has to wait for the frame's submission.
/// </summary>
internal sealed class D3D11Swapchain : GpuSwapchain
{
    internal D3D11Swapchain(D3D11Backend backend, nint windowHandle, in SwapchainDescription description, string? label)
        : base(description, label)
    {
        _backend = backend;

        using var dxgiDevice = backend.Device.QueryInterface<DXGI.Device2>();
        using var adapter = dxgiDevice.Adapter;
        using var factory = adapter.GetParent<DXGI.Factory2>();

        var swapChainDescription = new DXGI.SwapChainDescription1
                                       {
                                           Width = description.Width,
                                           Height = description.Height,
                                           Format = Convert.ToDxgi(description.Format),
                                           Stereo = false,
                                           SampleDescription = new DXGI.SampleDescription(1, 0),
                                           Usage = DXGI.Usage.RenderTargetOutput,
                                           BufferCount = Math.Max(2, description.BufferCount),
                                           Scaling = DXGI.Scaling.Stretch,

                                           // Flip model: the only one that composes without a copy, and what
                                           // the waitable object needs.
                                           SwapEffect = DXGI.SwapEffect.FlipDiscard,
                                           Flags = description.WaitableForLatency
                                                       ? DXGI.SwapChainFlags.FrameLatencyWaitAbleObject
                                                       : DXGI.SwapChainFlags.None,
                                       };

        _swapChain = new DXGI.SwapChain1(factory, backend.Device, windowHandle, ref swapChainDescription);

        if (!description.AllowModeSwitch)
            factory.MakeWindowAssociation(windowHandle, DXGI.WindowAssociationFlags.IgnoreAltEnter);

        if (description.WaitableForLatency)
        {
            _swapChain2 = _swapChain.QueryInterfaceOrNull<DXGI.SwapChain2>();

            if (_swapChain2 != null)
            {
                _swapChain2.MaximumFrameLatency = 1;

                // Every query opens a new handle for the caller to close, so it is taken once and kept; it stays
                // valid across ResizeBuffers as long as the flag is kept.
                _latencyWaitable = _swapChain2.FrameLatencyWaitableObject;
            }
        }

        AcquireBackBuffer();
    }

    public override GpuTexture BackBuffer => _backBuffer!;

    public override void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == Description.Width && height == Description.Height))
            return;

        // Every view of the old back buffer dies with it, which is why the render loop recreates them after
        // a resize rather than caching them across one.
        _backBuffer?.Dispose();
        _backBuffer = null;

        _swapChain.ResizeBuffers(Math.Max(2, Description.BufferCount), width, height, Convert.ToDxgi(Description.Format),
                                 Description.WaitableForLatency ? DXGI.SwapChainFlags.FrameLatencyWaitAbleObject : DXGI.SwapChainFlags.None);

        Description = Description with { Width = width, Height = height };
        AcquireBackBuffer();
    }

    public override void Present(int syncInterval) => _swapChain.Present(syncInterval, DXGI.PresentFlags.None);

    public override void WaitForFrameLatency()
    {
        if (_latencyWaitable != nint.Zero)
            WaitForSingleObject(_latencyWaitable, 1000);
    }

    private void AcquireBackBuffer()
    {
        var texture = _swapChain.GetBackBuffer<SharpDX.Direct3D11.Texture2D>(0);
        var description = new TextureDescription
                              {
                                  Dimension = TextureDimension.Texture2D,
                                  Width = Description.Width,
                                  Height = Description.Height,
                                  Depth = 1,
                                  ArraySize = 1,
                                  MipLevels = 1,
                                  Format = Description.Format,
                                  Samples = new SampleDescription(1, 0),
                                  Usage = TextureUsage.RenderTarget | TextureUsage.CopySource | TextureUsage.CopyDestination,
                                  Memory = MemoryKind.DeviceLocal,
                              };

        _backBuffer = new D3D11Texture(_backend.Device, texture, description, "back buffer");
    }

    protected override void ReleaseWhenRetired()
    {
        _backBuffer?.Dispose();
        if (_latencyWaitable != nint.Zero)
            CloseHandle(_latencyWaitable);

        _swapChain2?.Dispose();
        _swapChain.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    private readonly D3D11Backend _backend;
    private readonly DXGI.SwapChain1 _swapChain;
    private readonly DXGI.SwapChain2? _swapChain2;
    private readonly nint _latencyWaitable;
    private D3D11Texture? _backBuffer;
}

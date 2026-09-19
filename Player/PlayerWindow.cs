#nullable enable
using System;
using System.Drawing;
using SDL;
using SharpDX.DXGI;
using T3.SdlPlatform;
using static SDL.SDL3;
using Device = SharpDX.Direct3D11.Device;
using RenderTargetView = SharpDX.Direct3D11.RenderTargetView;
using Resource = SharpDX.Direct3D11.Resource;

namespace T3.Player;

/// <summary>
/// An SDL window with the Direct3D 11 swap chain that presents into it. Used for the main window and for
/// every additional output window.
/// </summary>
internal sealed unsafe class PlayerWindow : IDisposable
{
    /// <summary>Creates the window hidden and centered on <paramref name="display"/>; <see cref="Show"/> maps it.</summary>
    public PlayerWindow(string title, Size clientSizeInPixels, SDL_DisplayID display, string? iconPath)
    {
        _requestedClientSize = clientSizeInPixels;
        _window = SDL_CreateWindow(title, clientSizeInPixels.Width, clientSizeInPixels.Height,
                                   SDL_WindowFlags.SDL_WINDOW_HIDDEN | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY);
        if (_window == null)
            throw new InvalidOperationException($"SDL_CreateWindow failed: {SDL_GetError()}");

        Id = SDL_GetWindowID(_window);
        MoveToDisplay(display);
        if (iconPath != null)
            SdlWindowIcon.TrySet(_window, iconPath);
    }

    public SDL_WindowID Id { get; }
    public SwapChain SwapChain { get; private set; } = null!;
    public SharpDX.Direct3D11.Texture2D BackBuffer { get; private set; } = null!;
    public RenderTargetView RenderTargetView { get; private set; } = null!;
    public Size BackBufferSize { get; private set; }

    public void Show()
    {
        SDL_ShowWindow(_window);
        SDL_SyncWindow(_window);

        // Windows sizes windows in pixels; Wayland and macOS in points. Only the pixel size is known to match
        // the requested resolution, so a scaled display gets a smaller window to compensate.
        var isFullscreen = (SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0;
        var pixelSize = GetPixelSize();
        if (isFullscreen || pixelSize.Width == 0 || pixelSize.Width == _requestedClientSize.Width)
            return;

        int width, height;
        SDL_GetWindowSize(_window, &width, &height);
        var scale = (float)pixelSize.Width / width;
        SDL_SetWindowSize(_window, (int)MathF.Round(_requestedClientSize.Width / scale), (int)MathF.Round(_requestedClientSize.Height / scale));
        SDL_SyncWindow(_window);
    }

    public void MoveToDisplay(SDL_DisplayID display)
    {
        // Wayland does not let applications position windows; fullscreen still lands on this display.
        var centered = SDL_WINDOWPOS_CENTERED_DISPLAY(display);
        SDL_SetWindowPosition(_window, centered, centered);
    }

    /// <summary>
    /// Borderless fullscreen at the desktop resolution. Exclusive mode switching is avoided on purpose: it drops
    /// back to windowed on focus loss and needs a swap chain rebuild after every change.
    /// </summary>
    public void SetFullscreen(bool fullscreen)
    {
        SDL_SetWindowFullscreenMode(_window, null);
        SDL_SetWindowFullscreen(_window, fullscreen);
        SDL_SyncWindow(_window);
    }

    /// <summary>The pointer position in 0..1 window coordinates, from an SDL event's window coordinates.</summary>
    public System.Numerics.Vector2 ToRelativePosition(float x, float y)
    {
        int width, height;
        SDL_GetWindowSize(_window, &width, &height);
        return width > 0 && height > 0
                   ? new System.Numerics.Vector2(x / width, y / height)
                   : System.Numerics.Vector2.Zero;
    }

    public void CreateSwapChain(Device device)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The player renders with Direct3D 11, which needs Windows.");

        var windowHandle = SDL_GetPointerProperty(SDL_GetWindowProperties(_window), SDL_PROP_WINDOW_WIN32_HWND_POINTER, IntPtr.Zero);
        var size = GetPixelSize();
        var description = new SwapChainDescription
                              {
                                  BufferCount = BufferCount,
                                  ModeDescription = new ModeDescription(size.Width, size.Height, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                                  IsWindowed = true,
                                  OutputHandle = windowHandle,
                                  SampleDescription = new SampleDescription(1, 0),
                                  SwapEffect = SwapEffect.FlipDiscard,
                                  Flags = SwapChainFlags.AllowModeSwitch,
                                  Usage = Usage.RenderTargetOutput,
                              };

        using var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>();
        using var adapter = dxgiDevice.Adapter;
        using var factory = adapter.GetParent<Factory>();
        SwapChain = new SwapChain(factory, device, description);

        // SDL owns the window; DXGI must not react to Alt+Enter or other window messages on its own.
        factory.MakeWindowAssociation(windowHandle, WindowAssociationFlags.IgnoreAll);
        CreateBackBufferViews(device);
    }

    /// <summary>
    /// Resizes the swap chain when the window's pixel size changed (fullscreen toggle, DPI change). Call once
    /// per frame before rendering. <paramref name="releaseBackBufferReferences"/> runs before the resize, because
    /// ResizeBuffers fails while anything still references the back buffer.
    /// </summary>
    public void EnsureBackBufferSize(Device device, Action? releaseBackBufferReferences)
    {
        var size = GetPixelSize();
        if (size == BackBufferSize || size.Width == 0 || size.Height == 0)
            return;

        // A render target view still bound to the output merger also counts as a reference, and leaving it
        // bound can escalate to DXGI_ERROR_DEVICE_HUNG on the next Present.
        device.ImmediateContext.OutputMerger.SetTargets((RenderTargetView?)null);
        releaseBackBufferReferences?.Invoke();
        RenderTargetView.Dispose();
        BackBuffer.Dispose();

        SwapChain.ResizeBuffers(BufferCount, size.Width, size.Height, Format.Unknown, SwapChain.Description.Flags);
        CreateBackBufferViews(device);
    }

    public void Dispose()
    {
        RenderTargetView?.Dispose();
        BackBuffer?.Dispose();
        SwapChain?.Dispose();
        SDL_DestroyWindow(_window);
    }

    private void CreateBackBufferViews(Device device)
    {
        BackBuffer = Resource.FromSwapChain<SharpDX.Direct3D11.Texture2D>(SwapChain, 0);
        RenderTargetView = new RenderTargetView(device, BackBuffer);
        BackBufferSize = new Size(BackBuffer.Description.Width, BackBuffer.Description.Height);
    }

    private Size GetPixelSize()
    {
        int width, height;
        SDL_GetWindowSizeInPixels(_window, &width, &height);
        return new Size(width, height);
    }

    private const int BufferCount = 3;

    private readonly SDL_Window* _window;
    private readonly Size _requestedClientSize;
}

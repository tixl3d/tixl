#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using SDL;
using T3.Core.Logging;
using T3.Graphics;
using T3.Graphics.Compat;
using T3.SdlPlatform;
using static SDL.SDL3;
using Device = T3.Graphics.Compat.Device;
using RenderTargetView = T3.Graphics.Compat.RenderTargetView;
using Resource = T3.Graphics.Compat.Resource;

namespace T3.Player;

/// <summary>
/// An SDL window with the swap chain that presents into it. Used for the main window and for every
/// additional output window.
/// </summary>
internal sealed unsafe class PlayerWindow : IDisposable
{
    /// <summary>Creates the window hidden and centered on <paramref name="display"/>; <see cref="Show"/> maps it.</summary>
    public PlayerWindow(string title, Size clientSizeInPixels, SDL_DisplayID display, string? iconPath)
    {
        _requestedClientSize = clientSizeInPixels;

        // A surface can only be created from a window that asked for Vulkan when it was made, so the flag has
        // to match the backend the player will pick.
        var flags = SDL_WindowFlags.SDL_WINDOW_HIDDEN | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY;
        if (!OperatingSystem.IsWindows())
            flags |= SDL_WindowFlags.SDL_WINDOW_VULKAN;

        _window = SDL_CreateWindow(title, clientSizeInPixels.Width, clientSizeInPixels.Height, flags);
        if (_window == null)
            throw new InvalidOperationException($"SDL_CreateWindow failed: {SDL_GetError()}");

        Id = SDL_GetWindowID(_window);
        MoveToDisplay(display);
        if (iconPath != null)
            SdlWindowIcon.TrySet(_window, iconPath);
    }

    public SDL_WindowID Id { get; }
    public SwapChain SwapChain { get; private set; } = null!;
    public T3.Graphics.Compat.Texture2D BackBuffer { get; private set; } = null!;
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

    /// <summary>
    /// Creates the chain this window presents from. Which handle the backend needs is its own business: the
    /// window hands over the Win32 one and a way to make a Vulkan surface, and the backend takes what it uses.
    /// </summary>
    public void CreateSwapChain(Device device)
    {
        var size = GetPixelSize();
        var target = new SurfaceTarget
                         {
                             Win32Window = OperatingSystem.IsWindows()
                                               ? SDL_GetPointerProperty(SDL_GetWindowProperties(_window), SDL_PROP_WINDOW_WIN32_HWND_POINTER,
                                                                        IntPtr.Zero)
                                               : IntPtr.Zero,
                             CreateVulkanSurface = CreateVulkanSurface,
                         };

        SwapChain = SwapChain.TryCreate(device,
                                        new SwapChainDescription
                                            {
                                                Width = size.Width,
                                                Height = size.Height,
                                                Format = Format.R8G8B8A8_UNorm,
                                                BufferCount = BufferCount,
                                                AllowModeSwitch = true,
                                            },
                                        target, "player window")
                    ?? throw new InvalidOperationException("Could not create a swap chain for the player window.");

        CreateBackBufferViews(device);
    }

    /// <summary>
    /// The Vulkan instance extensions this platform's surface needs. Only SDL knows them, and they have to be
    /// enabled when the instance is created, which happens before any window hands out a surface.
    /// </summary>
    public static IReadOnlyList<string> GetVulkanInstanceExtensions()
    {
        uint count;
        var names = SDL_Vulkan_GetInstanceExtensions(&count);
        if (names == null)
        {
            Log.Warning($"SDL could not report the Vulkan instance extensions: {SDL_GetError()}");
            return [];
        }

        var extensions = new List<string>((int)count);
        for (var i = 0; i < count; i++)
        {
            var name = Marshal.PtrToStringUTF8((nint)names[i]);
            if (!string.IsNullOrEmpty(name))
                extensions.Add(name);
        }

        return extensions;
    }

    private nint CreateVulkanSurface(nint instance)
    {
        VkSurfaceKHR_T* surface = null;
        return SDL_Vulkan_CreateSurface(_window, (VkInstance_T*)instance, null, &surface) ? (nint)surface : IntPtr.Zero;
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

        SwapChain.ResizeBuffers(size.Width, size.Height);
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
        BackBuffer = SwapChain.GetBackBuffer();
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

#nullable enable
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using T3.Graphics.Compat;
using T3.Graphics;
using T3.Core.DataTypes.Vector;
using T3.Core.Resource;
using T3.Editor.Gui.Styling;
using T3.SdlPlatform;
using static SDL.SDL3;
using Device = T3.Graphics.Compat.Device;
using Resource = T3.Graphics.Compat.Resource;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.App;

/// <summary>
/// An SDL window with the swap chain that presents into it: the editor itself, the secondary viewer, and one per
/// display an output is bound to. Sizes are in pixels throughout, as the swap chain and ImGui count them.
/// </summary>
internal sealed unsafe class AppWindow
{
    public SDL_WindowID Id { get; }
    public IntPtr HwndHandle => SdlSurface.GetWin32Handle(_window);
    public Int2 Size => new(Width, Height);
    public int Width => GetPixelSize().X;
    public int Height => GetPixelSize().Y;
    public bool IsFullScreen { get; private set; }

    /// <summary>
    /// Pixels per window point: 1 on Windows, 2 on a Retina or 200% Wayland display. SDL reports the pointer in
    /// points, so input has to be scaled by this to land where ImGui draws.
    /// </summary>
    public float PixelDensity
    {
        get
        {
            var density = SDL_GetWindowPixelDensity(_window);
            return density > 0 ? density : 1f;
        }
    }

    internal SwapChain SwapChain => _swapChain;
    internal RenderTargetView RenderTargetView => _renderTargetView;

    /// <summary>
    /// Set before swap-chain creation. When true, the swap chain is created with a frame-latency waitable object
    /// and <see cref="WaitForFrameLatency"/> paces the loop, instead of the accidental pacing the viewer's second
    /// Present used to provide.
    /// </summary>
    internal bool UseFrameLatencyWaitable { get; set; }

    internal bool IsMinimized => (SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0;
    public Texture2D? Texture { get; set; }
    public Texture2D BackBufferTexture => _backBufferTexture;

    internal AppWindow(string windowTitle, bool disableClose)
    {
        _disableClose = disableClose;

        // A surface can only be created from a window that asked for Vulkan when it was made, so the flag has to
        // match the backend the editor picks.
        var flags = SDL_WindowFlags.SDL_WINDOW_HIDDEN | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY | SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        if (!OperatingSystem.IsWindows())
            flags |= SDL_WindowFlags.SDL_WINDOW_VULKAN;

        var initialSize = disableClose ? new Int2(640, 360 + 20) : new Int2(640, 480);
        _window = SDL_CreateWindow(windowTitle, initialSize.X, initialSize.Y, flags);
        if (_window == null)
            throw new InvalidOperationException($"SDL_CreateWindow failed: {SDL_GetError()}");

        Id = SDL_GetWindowID(_window);
        SdlWindowIcon.TrySet(_window, Path.Combine(SharedResources.EditorResourcesDirectory, "images", "t3.ico"));
    }

    public void SetVisible(bool isVisible)
    {
        // Called every frame for the viewer; SDL maps and unmaps windows even when nothing changes.
        var isHidden = (SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_HIDDEN) != 0;
        if (isVisible != isHidden)
            return;

        if (isVisible)
        {
            Show();
        }
        else
        {
            SDL_HideWindow(_window);
        }
    }

    public void SetSizeable()
    {
        if (IsFullScreen)
        {
            // SDL restores the windowed bounds it had before going fullscreen.
            SDL_SetWindowFullscreen(_window, false);
            IsFullScreen = false;
        }

        SetBorderStyleSizable();
    }

    public void Show()
    {
        SDL_ShowWindow(_window);
        SDL_SyncWindow(_window);

        // The pixel density of a hidden window is a guess on Wayland; now that the window is mapped the size
        // requested in pixels can be met.
        if (_requestedPixelSize is { } requested && !IsFullScreen && !IsMaximized && GetPixelSize() != requested)
            SetSize(requested.X, requested.Y);
    }

    public Vector2 GetDpi()
    {
        var scale = SDL_GetWindowDisplayScale(_window);
        var dpi = (scale > 0 ? scale : 1f) * 96f;
        return new Vector2(dpi, dpi);
    }

    /// <summary>
    /// Borderless fullscreen on a display, at the desktop resolution. Exclusive mode switching is avoided on
    /// purpose: it drops back to windowed on focus loss and needs a swap chain rebuild after every change.
    /// </summary>
    internal void SetFullScreen(int screenIndex)
    {
        // Checked before anything changes: a display unplugged since its binding was saved must not leave a
        // borderless window stuck at its old bounds.
        if (screenIndex < 0 || screenIndex >= DisplayCount)
        {
            Log.Error($"Attempt to set out of bounds screen #{screenIndex} to fullscreen");
            return;
        }

        // Wayland does not let applications position windows; fullscreen still lands on the display it names.
        var centered = SDL_WINDOWPOS_CENTERED_DISPLAY(_displayProvider.GetDisplayId(screenIndex));
        SDL_SetWindowPosition(_window, centered, centered);
        SDL_SetWindowFullscreenMode(_window, null);
        SDL_SetWindowFullscreen(_window, true);
        SDL_SyncWindow(_window);
        IsFullScreen = true;
    }

    /// <summary>The displays <see cref="SetFullScreen"/> can target, in the order it indexes them.</summary>
    internal static int DisplayCount => _displayProvider.GetDisplays().Count;

    /// <summary>The index of the display the window is on, as <see cref="SetFullScreen"/> takes it; 0 if unknown.</summary>
    internal int GetDisplayIndex()
    {
        var display = SDL_GetDisplayForWindow(_window);
        var displays = _displayProvider.GetDisplays();
        for (var index = 0; index < displays.Count; index++)
        {
            if (_displayProvider.GetDisplayId(index) == display)
                return index;
        }

        return 0;
    }

    /// <summary>
    /// Creates the chain this window presents from. Which handle the backend needs is its own business: the
    /// window hands over the Win32 one and a way to make a Vulkan surface, and the backend takes what it uses.
    /// </summary>
    internal void InitViewSwapChain()
    {
        var size = GetPixelSize();
        var target = new SurfaceTarget
                         {
                             Win32Window = SdlSurface.GetWin32Handle(_window),
                             CreateVulkanSurface = CreateVulkanSurface,
                         };

        _swapChain = SwapChain.TryCreate(_device,
                                         new SwapChainDescription
                                             {
                                                 Width = size.X,
                                                 Height = size.Y,
                                                 Format = Format.R8G8B8A8_UNorm,
                                                 BufferCount = 3,
                                                 UseFrameLatencyWaitableObject = UseFrameLatencyWaitable,
                                             },
                                         target, "editor window")
                     ?? throw new InvalidOperationException("Could not create a swap chain for the editor window.");
    }

    private nint CreateVulkanSurface(nint instance) => SdlSurface.CreateVulkanSurface(_window, instance);

    /// <summary>
    /// Picks this frame's back buffer, binds it and clears it. Resizes the swap chain first when the window's
    /// pixel size changed. Safe to call more than once per frame: the image stays the same until it is presented.
    /// </summary>
    internal void PrepareRenderingFrame()
    {
        EnsureBackBufferSize();
        AcquireBackBuffer();

        var width = _backBufferTexture.Description.Width;
        var height = _backBufferTexture.Description.Height;
        _deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        _deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, width, height, 0.0f, 1.0f));
        _deviceContext.OutputMerger.SetTargets(_renderTargetView);

        _deviceContext.ClearRenderTargetView(_renderTargetView, UiColors.WindowBackground.Rgba);
    }

    /// <summary>
    /// Runs the editor until it is asked to exit, rendering once per pass after draining SDL's event queue.
    /// Only the main window runs a loop; the others are drawn from inside it.
    /// </summary>
    internal void RunRenderLoop(Action callback)
    {
        _renderCallback = callback;
        Show();
        if (_startMaximized)
            SDL_MaximizeWindow(_window);

        SDL_StartTextInput(_window);

        _liveResizeWindow = this;
        SDL_AddEventWatch(&RenderDuringLiveResize, IntPtr.Zero);

        IsLoopRunning = true;
        SDL_Event sdlEvent;
        while (!_exitRequested)
        {
            while (SDL_PollEvent(&sdlEvent))
            {
                HandleEvent(sdlEvent);
            }

            if (_exitRequested)
                break;

            RenderFrame();
        }

        IsLoopRunning = false;
        SDL_RemoveEventWatch(&RenderDuringLiveResize, IntPtr.Zero);
        _liveResizeWindow = null;
        SDL_StopTextInput(_window);
    }

    internal static bool IsLoopRunning { get; private set; }

    /// <summary>Ends <see cref="RunRenderLoop"/> after the current frame, without asking the close handler.</summary>
    internal static void RequestExit() => _exitRequested = true;

    internal void SetSize(int width, int height)
    {
        _requestedPixelSize = new Int2(width, height);

        // SDL sizes windows in points; the editor asks for pixels.
        var density = PixelDensity;
        SDL_SetWindowSize(_window, (int)MathF.Round(width / density), (int)MathF.Round(height / density));
    }

    internal void SetBorderStyleSizable()
    {
        SDL_SetWindowBordered(_window, true);
        SDL_SetWindowResizable(_window, true);
    }

    /// <param name="maximized">Applied when the window is first shown.</param>
    /// <param name="canClose">
    /// Asked when the user closes the window; false keeps it open. A window without one ignores close requests —
    /// the viewer's, because closing it used to crash in the swap chain.
    /// </param>
    internal void InitializeWindow(bool maximized, Func<bool>? canClose)
    {
        _startMaximized = maximized;
        _canClose = canClose;
    }

    internal void SetDevice(Device device, DeviceContext deviceContext)
    {
        if (_hasSetDevice)
            throw new InvalidOperationException("Device has already been set");

        _hasSetDevice = true;
        _device = device;
        _deviceContext = deviceContext;
    }

    /// <summary>
    /// Blocks until the swap chain signals that the next frame may be submitted. Call once at the very start of
    /// each frame. No-op unless <see cref="UseFrameLatencyWaitable"/> was set before the swap chain was made.
    /// </summary>
    public void WaitForFrameLatency()
    {
        if (UseFrameLatencyWaitable)
            _swapChain?.WaitForFrameLatency();
    }

    internal void Release()
    {
        DisposeBackBufferViews();
        _swapChain?.Dispose();
        SDL_DestroyWindow(_window);
    }

    public void SetTexture(Texture2D texture)
    {
        Texture = texture;
    }

    private void HandleEvent(in SDL_Event sdlEvent)
    {
        switch (sdlEvent.Type)
        {
            // Closing the last window or a signal: the editor decides as for its own close button.
            case SDL_EventType.SDL_EVENT_QUIT:
                RequestClose();
                break;

            case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                if (sdlEvent.window.windowID == Id)
                    RequestClose();

                break;
        }

        SdlImGuiInput.ProcessEvent(sdlEvent);
    }

    private void RequestClose()
    {
        if (_disableClose)
            return;

        if (_canClose == null || _canClose())
            _exitRequested = true;
    }

    private void RenderFrame()
    {
        _isRendering = true;
        try
        {
            _renderCallback?.Invoke();
        }
        finally
        {
            _isRendering = false;
        }
    }

    /// <summary>
    /// Windows runs a modal loop while a window is dragged or resized, and the editor's own loop stalls until the
    /// mouse is released. SDL reports each step of it from inside that loop, and a frame drawn here keeps the
    /// window's content live instead of stretched.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static SDLBool RenderDuringLiveResize(IntPtr userData, SDL_Event* sdlEvent)
    {
        var window = _liveResizeWindow;
        if (window == null
            || window._isRendering
            || sdlEvent->Type != SDL_EventType.SDL_EVENT_WINDOW_EXPOSED
            || sdlEvent->window.windowID != window.Id
            || sdlEvent->window.data1 == 0)
        {
            return true;
        }

        // An exception must not unwind into SDL's native frame.
        try
        {
            window.RenderFrame();
        }
        catch (Exception e)
        {
            Log.Error($"Rendering during a live resize failed: {e}");
        }

        return true;
    }

    private bool IsMaximized => (SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_MAXIMIZED) != 0;

    private Int2 GetPixelSize()
    {
        int width, height;
        SDL_GetWindowSizeInPixels(_window, &width, &height);
        return new Int2(width, height);
    }

    private void EnsureBackBufferSize()
    {
        var size = GetPixelSize();
        if (size.X == 0 || size.Y == 0 || (size.X == _swapChain.Width && size.Y == _swapChain.Height))
            return;

        lock (StallWatchdog.PresentLock)
        {
            // ResizeBuffers requires that no reference to the back buffer survives - including a binding on the
            // output merger. A still-bound RTV leaves the pipeline in undefined state which can escalate to
            // DXGI_ERROR_DEVICE_HUNG on the next Present.
            _deviceContext.OutputMerger.SetTargets((RenderTargetView?)null);
            DisposeBackBufferViews();
            _swapChain.ResizeBuffers(size.X, size.Y);
        }
    }

    /// <summary>
    /// Asking the swap chain for its back buffer is what acquires one under Vulkan, so it happens every frame —
    /// holding on to the first one renders into an image that is never presented. The views are kept per image
    /// because a chain cycles through the same few.
    /// </summary>
    private void AcquireBackBuffer()
    {
        _backBufferTexture = _swapChain.GetBackBuffer();

        if (!_viewsPerBackBuffer.TryGetValue(_backBufferTexture, out var view))
        {
            view = new RenderTargetView(_device, _backBufferTexture);
            _viewsPerBackBuffer[_backBufferTexture] = view;
        }

        _renderTargetView = view;
    }

    private void DisposeBackBufferViews()
    {
        // Only the views: the back buffers belong to the swap chain, which releases them when it resizes.
        foreach (var view in _viewsPerBackBuffer.Values)
        {
            view.Dispose();
        }

        _viewsPerBackBuffer.Clear();
    }

    private static readonly SdlDisplayProvider _displayProvider = new();
    private static AppWindow? _liveResizeWindow;
    private static bool _exitRequested;

    private readonly SDL_Window* _window;
    private readonly bool _disableClose;
    private readonly Dictionary<Texture2D, RenderTargetView> _viewsPerBackBuffer = [];
    private bool _hasSetDevice;
    private Device _device = null!;
    private DeviceContext _deviceContext = null!;
    private SwapChain _swapChain = null!;
    private RenderTargetView _renderTargetView = null!;
    private Texture2D _backBufferTexture = null!;
    private Action? _renderCallback;
    private Func<bool>? _canClose;
    private Int2? _requestedPixelSize;
    private bool _startMaximized;
    private bool _isRendering;
}

#nullable enable
using System;
using System.Collections.Generic;
using T3.Graphics.Compat;
using T3.Core.Resource;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Editor.App;

/// <summary>
/// The borderless windows that carry output composites onto displays — one per display an output is bound to,
/// claimed afresh each frame. A window whose output goes away is hidden rather than closed: its swap chain is
/// driven by the render loop, and closing one is what the displayWindow window's disabled close button already guards
/// against. Displays are few and long-lived, so a hidden window costs less than the teardown would.
/// </summary>
internal static class OutputWindowHandling
{
    /// <summary>One display's window, and a view of whichever composite is currently on it.</summary>
    internal sealed class DisplayWindow
    {
        public AppWindow Window { get; }

        public DisplayWindow(AppWindow window)
        {
            Window = window;
        }

        /// <summary>
        /// The current composite as a shader resource, or null while nothing has been shown yet. It is rebuilt
        /// only when a different texture arrives; the texture's pointer is remembered rather than read back from
        /// the view, because reading a view's resource hands out a COM reference that would have to be released.
        /// </summary>
        public ShaderResourceView? EnsureTextureView()
        {
            var texture = Window.Texture;
            if (texture == null || texture.IsDisposed)
                return null;

            if (_textureView == null || _textureView.IsDisposed || _viewedTexture != texture.NativePointer)
            {
                _textureView?.Dispose();
                _textureView = new ShaderResourceView(ResourceManager.Device, texture);
                _viewedTexture = texture.NativePointer;
            }

            return _textureView;
        }

        /// <summary>
        /// Forgets the composite and its view. The texture is about to be freed, and a later one can land on the
        /// same address, where the remembered pointer would match and hand out a view of the wrong resource.
        /// </summary>
        public void ReleaseTexture()
        {
            _textureView?.Dispose();
            _textureView = null;
            _viewedTexture = IntPtr.Zero;
            Window.Texture = null;
        }

        public void SetVisible(bool isVisible)
        {
            if (_isVisible == isVisible)
                return;

            _isVisible = isVisible;
            Window.SetVisible(isVisible);
        }

        internal bool IsClaimed;
        private ShaderResourceView? _textureView;
        private IntPtr _viewedTexture;
        private bool _isVisible;
    }

    /// <summary>The windows an output claimed this frame, in the order the outputs were walked.</summary>
    public static IReadOnlyList<DisplayWindow> Presenting => _presenting;

    /// <summary>Drops the previous frame's claims; a window stays up only if <see cref="Present"/> claims it again.</summary>
    public static void BeginFrame()
    {
        _presenting.Clear();
    }

    /// <summary>
    /// Shows a composite full-screen on a display, opening that display's window the first time it is asked for.
    /// The first output to claim a display keeps it for the frame, so two outputs bound to the same display
    /// don't flicker against each other.
    /// </summary>
    public static void Present(int displayIndex, Texture2D? composite)
    {
        var displayWindow = GetOrCreate(displayIndex);
        if (displayWindow == null || displayWindow.IsClaimed)
            return;

        displayWindow.IsClaimed = true;

        // Assigning null would trip the implicit conversion to the SharpDX texture; keep the last frame instead.
        if (composite is { IsDisposed: false })
            displayWindow.Window.Texture = composite;

        _presenting.Add(displayWindow);
    }

    /// <summary>Hides every window no output claimed; runs once after the outputs have been walked.</summary>
    public static void EndFrame()
    {
        foreach (var displayWindow in _windows.Values)
        {
            displayWindow.SetVisible(displayWindow.IsClaimed);
            displayWindow.IsClaimed = false;
        }
    }

    /// <summary>Takes every output window down — the setup that drove them is going away.</summary>
    public static void HideAll()
    {
        _presenting.Clear();
        foreach (var displayWindow in _windows.Values)
        {
            displayWindow.SetVisible(false);
            displayWindow.ReleaseTexture();
            displayWindow.IsClaimed = false;
        }
    }

    /// <summary>Frees every display window's swap chain; the process is shutting down.</summary>
    public static void Release()
    {
        _presenting.Clear();
        foreach (var displayWindow in _windows.Values)
        {
            displayWindow.ReleaseTexture();
            displayWindow.Window.Release();
        }

        _windows.Clear();
    }

    /// <summary>
    /// The window for a display, created and put full-screen on first use. Null for a display that isn't there —
    /// a binding outlives the display it names, so an unplugged projector simply presents nothing.
    /// </summary>
    private static DisplayWindow? GetOrCreate(int displayIndex)
    {
        if (_windows.TryGetValue(displayIndex, out var existing))
            return existing;

        // Listing the displays allocates, so it is consulted only when a window is actually being opened.
        if (displayIndex < 0 || displayIndex >= AppWindow.DisplayCount)
            return null;

        var window = ProgramWindows.CreateViewerWindow($"TiXL Output {displayIndex + 1}", 640, 360);
        window.SetFullScreen(displayIndex);
        var displayWindow = new DisplayWindow(window);
        _windows[displayIndex] = displayWindow;
        return displayWindow;
    }

    private static readonly Dictionary<int, DisplayWindow> _windows = [];
    private static readonly List<DisplayWindow> _presenting = [];
}

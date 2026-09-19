using System.Diagnostics;
using System.Drawing;
using SDL;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>
/// <see cref="ICoreSystemUiService"/> for applications that run their own SDL event loop.
/// </summary>
public sealed class SdlCoreUi : ICoreSystemUiService
{
    /// <summary>
    /// Set by <see cref="ExitApplication"/> while an event loop runs; the loop checks it every frame.
    /// </summary>
    public bool QuitRequested { get; private set; }

    /// <summary>Tells <see cref="ExitApplication"/> whether a loop is there to act on <see cref="QuitRequested"/>.</summary>
    public bool IsEventLoopRunning { get; set; }

    public ICursor Cursor { get; } = new SdlCursor();

    public void OpenWithDefaultApplication(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("Uri is empty", nameof(uri));

        // SDL_OpenURL wants a URL; plain paths become file:// URIs.
        var url = Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.AbsoluteUri : new Uri(Path.GetFullPath(uri)).AbsoluteUri;
        if (!SDL_OpenURL(url))
            throw new InvalidOperationException($"Could not open \"{uri}\": {SDL_GetError()}");
    }

    public void RevealInFileBrowser(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is empty", nameof(path));

        var fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows() && File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo("explorer", $"/select,\"{fullPath}\""));
            return;
        }

        // Selecting a file needs a platform file-manager API; opening its folder works everywhere.
        OpenWithDefaultApplication(File.Exists(fullPath) ? Path.GetDirectoryName(fullPath)! : fullPath);
    }

    public void ExitApplication()
    {
        if (IsEventLoopRunning)
        {
            QuitRequested = true;
            return;
        }

        Environment.Exit(1);
    }

    public void ExitThread()
    {
        throw new NotSupportedException("SDL applications exit through ExitApplication");
    }

    public void SetUnhandledExceptionMode(bool throwException)
    {
        // A WinForms concept: without a WinForms message loop, unhandled exceptions always propagate.
    }

    private sealed class SdlCursor : ICursor
    {
        public Point Position
        {
            get
            {
                float x, y;
                unsafe
                {
                    SDL_GetGlobalMouseState(&x, &y);
                }

                return new Point((int)x, (int)y);
            }
        }

        public MouseButtons ButtonState
        {
            get
            {
                SDL_MouseButtonFlags flags;
                unsafe
                {
                    flags = SDL_GetGlobalMouseState(null, null);
                }

                var buttons = MouseButtons.None;
                if ((flags & SDL_MouseButtonFlags.SDL_BUTTON_LMASK) != 0)
                    buttons |= MouseButtons.Left;

                if ((flags & SDL_MouseButtonFlags.SDL_BUTTON_RMASK) != 0)
                    buttons |= MouseButtons.Right;

                if ((flags & SDL_MouseButtonFlags.SDL_BUTTON_MMASK) != 0)
                    buttons |= MouseButtons.Middle;

                if ((flags & SDL_MouseButtonFlags.SDL_BUTTON_X1MASK) != 0)
                    buttons |= MouseButtons.Back;

                if ((flags & SDL_MouseButtonFlags.SDL_BUTTON_X2MASK) != 0)
                    buttons |= MouseButtons.Forward;

                return buttons;
            }
        }

        public void SetVisible(bool visible)
        {
            if (visible)
            {
                SDL_ShowCursor();
            }
            else
            {
                SDL_HideCursor();
            }
        }

        // Nothing in the player listens to these; the editor will raise them from its event loop.
        public event EventHandler<MouseButtonEventArgs>? ButtonChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<MouseState>? MouseChanged
        {
            add { }
            remove { }
        }
    }
}

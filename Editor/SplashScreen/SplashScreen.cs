using System.IO;
using SDL;
using StbImageSharp;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.Editor.SplashScreen;

/// <summary>
/// A borderless SDL window with the splash image, the version and the latest log line, shown while the editor
/// loads. Drawn with SDL's own renderer and debug font: nothing else that draws text is up yet.
/// </summary>
internal sealed unsafe class SplashScreen : ISplashScreen
{
    public void Show(string imagePath)
    {
        ImageResult image;
        try
        {
            using var stream = File.OpenRead(imagePath);
            image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception e)
        {
            Log.Warning($"Could not load the splash image: {e.Message}");
            return;
        }

        _window = SDL_CreateWindow("TiXL", image.Width, image.Height,
                                   SDL_WindowFlags.SDL_WINDOW_HIDDEN | SDL_WindowFlags.SDL_WINDOW_BORDERLESS
                                   | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY | SDL_WindowFlags.SDL_WINDOW_UTILITY);
        if (_window == null)
        {
            Log.Warning($"Could not open the splash screen: {SDL_GetError()}");
            return;
        }

        SDL_ShowWindow(_window);
        SDL_SyncWindow(_window);

        // The image is drawn for 96 dpi. A mapped window knows both its display's scale and how many pixels
        // make a point, which sizes it alike on Windows (points are pixels) and Wayland (points are scaled).
        var displayScale = SDL_GetWindowDisplayScale(_window);
        var pixelDensity = SDL_GetWindowPixelDensity(_window);
        if (displayScale > 0 && pixelDensity > 0)
        {
            var toPoints = displayScale / pixelDensity;
            SDL_SetWindowSize(_window, (int)MathF.Round(image.Width * toPoints), (int)MathF.Round(image.Height * toPoints));
        }

        SDL_SetWindowPosition(_window, (int)SDL_WINDOWPOS_CENTERED_MASK, (int)SDL_WINDOWPOS_CENTERED_MASK);
        SDL_SyncWindow(_window);

        _renderer = SDL_CreateRenderer(_window, (string?)null);
        if (_renderer == null)
        {
            Log.Warning($"Could not draw the splash screen: {SDL_GetError()}");
            return;
        }

        // RGBA bytes in memory are ABGR8888 on a little-endian machine.
        _texture = SDL_CreateTexture(_renderer, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888, SDL_TextureAccess.SDL_TEXTUREACCESS_STATIC,
                                     image.Width, image.Height);
        if (_texture != null)
        {
            fixed (byte* pixels = image.Data)
            {
                SDL_UpdateTexture(_texture, null, (IntPtr)pixels, image.Width * 4);
            }
        }

        // The debug font is 8 px; scaling the whole output keeps it readable on a high-dpi display.
        _textScale = MathF.Max(1, MathF.Round(displayScale > 0 ? displayScale : 1));
        _mainThreadId = Environment.CurrentManagedThreadId;
        Draw();
    }

    public void Close()
    {
        if (_texture != null)
            SDL_DestroyTexture(_texture);

        if (_renderer != null)
            SDL_DestroyRenderer(_renderer);

        if (_window != null)
            SDL_DestroyWindow(_window);

        _texture = null;
        _renderer = null;
        _window = null;
    }

    public void Dispose()
    {
        Close();
    }

    public ILogEntry.EntryLevel Filter { get; set; }

    public void ProcessEntry(ILogEntry entry)
    {
        var firstLine = entry.Message.Split("\n")[0];
        _logMessage = firstLine[..Math.Min(60, firstLine.Length)];

        // SDL draws on the thread that made the window; a message from another shows with the next one from it.
        if (Environment.CurrentManagedThreadId == _mainThreadId)
            Draw();
    }

    private void Draw()
    {
        if (_renderer == null)
            return;

        // Keeps Windows from marking a window that stops answering messages during a long load as hung.
        SDL_PumpEvents();

        SDL_SetRenderScale(_renderer, 1, 1);
        SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        SDL_RenderClear(_renderer);
        if (_texture != null)
            SDL_RenderTexture(_renderer, _texture, null, null);

        int width, height;
        SDL_GetCurrentRenderOutputSize(_renderer, &width, &height);
        SDL_SetRenderScale(_renderer, _textScale, _textScale);
        var logicalWidth = width / _textScale;
        var baseline = height / _textScale - DebugFontSize - Margin;

        SDL_SetRenderDrawColor(_renderer, 105, 105, 105, 255);
        SDL_RenderDebugText(_renderer, Margin, baseline, Program.FormattedEditorVersion);
        SDL_RenderDebugText(_renderer, logicalWidth - Margin - _logMessage.Length * DebugFontSize, baseline, _logMessage);

        SDL_RenderPresent(_renderer);
    }

    private const float DebugFontSize = 8;
    private const float Margin = 6;

    private SDL_Window* _window;
    private SDL_Renderer* _renderer;
    private SDL_Texture* _texture;
    private float _textScale = 1;
    private int _mainThreadId = -1;
    private string _logMessage = "Loading TiXL...";
}

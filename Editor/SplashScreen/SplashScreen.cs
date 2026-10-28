using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using SDL;
using StbImageSharp;
using T3.Core.Resource;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.Editor.SplashScreen;

/// <summary>
/// A borderless SDL window with the splash image, the version and the latest log line, shown while the editor
/// loads. Drawn with SDL's own renderer; the text glyphs come from an ImGui font atlas, which rasterizes without
/// an ImGui context. SDL's bold debug font remains the fallback if the font can't be loaded.
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
        _pixelScale = displayScale > 0 ? displayScale : 1;
        LoadFont(Path.Combine(SharedResources.EditorResourcesDirectory, "fonts", "Inter-Regular.ttf"), FontSize * _pixelScale);
        _mainThreadId = Environment.CurrentManagedThreadId;
        Draw();
    }

    private void LoadFont(string fontPath, float sizeInPixels)
    {
        if (!File.Exists(fontPath))
            return;

        var atlas = new ImFontAtlasPtr(ImGuiNative.ImFontAtlas_ImFontAtlas());
        var font = atlas.AddFontFromFileTTF(fontPath, sizeInPixels);
        if (font.NativePtr == null)
        {
            atlas.Destroy();
            return;
        }

        atlas.GetTexDataAsRGBA32(out IntPtr pixels, out var width, out var height);
        _fontTexture = SDL_CreateTexture(_renderer, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888, SDL_TextureAccess.SDL_TEXTUREACCESS_STATIC,
                                         width, height);
        if (_fontTexture == null)
        {
            atlas.Destroy();
            return;
        }

        SDL_UpdateTexture(_fontTexture, null, pixels, width * 4);
        SDL_SetTextureBlendMode(_fontTexture, SDL_BlendMode.SDL_BLENDMODE_BLEND);
        SDL_SetTextureColorMod(_fontTexture, 105, 105, 105);
        _fontAtlas = atlas;
        _font = font;
        _fontAtlasSize = new Vector2(width, height);
    }

    private float MeasureText(string text)
    {
        var width = 0f;
        foreach (var c in text)
        {
            var glyph = FindGlyph(c);
            if (glyph != null)
                width += glyph->AdvanceX;
        }

        return width;
    }

    private void DrawText(string text, float x, float top)
    {
        x = MathF.Round(x);
        top = MathF.Round(top);
        foreach (var c in text)
        {
            var glyph = FindGlyph(c);
            if (glyph == null)
                continue;

            var source = new SDL_FRect
                             {
                                 x = glyph->U0 * _fontAtlasSize.X,
                                 y = glyph->V0 * _fontAtlasSize.Y,
                                 w = (glyph->U1 - glyph->U0) * _fontAtlasSize.X,
                                 h = (glyph->V1 - glyph->V0) * _fontAtlasSize.Y,
                             };
            var target = new SDL_FRect
                             {
                                 x = x + glyph->X0,
                                 y = top + glyph->Y0,
                                 w = glyph->X1 - glyph->X0,
                                 h = glyph->Y1 - glyph->Y0,
                             };
            SDL_RenderTexture(_renderer, _fontTexture, &source, &target);
            x += glyph->AdvanceX;
        }
    }

    // ImGui.NET declares the native flags bitfield as three separate fields, which shifts
    // all following members. So the native layout is read directly.
    private NativeGlyph* FindGlyph(char c) => (NativeGlyph*)_font.FindGlyph(c).NativePtr;

    /// <summary>Memory layout of ImFontGlyph.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGlyph
    {
        public uint Flags;
        public float AdvanceX;
        public float X0, Y0, X1, Y1;
        public float U0, V0, U1, V1;
    }

    public void Close()
    {
        if (_fontTexture != null)
            SDL_DestroyTexture(_fontTexture);

        if (_fontAtlas.NativePtr != null)
            _fontAtlas.Destroy();

        if (_texture != null)
            SDL_DestroyTexture(_texture);

        if (_renderer != null)
            SDL_DestroyRenderer(_renderer);

        if (_window != null)
            SDL_DestroyWindow(_window);

        _fontTexture = null;
        _fontAtlas = default;
        _font = default;
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

        if (_fontTexture != null)
        {
            var margin = Margin * _pixelScale;
            var top = height - margin - _font.FontSize;
            DrawText(Program.FormattedEditorVersion, margin, top);
            DrawText(_logMessage, width - margin - MeasureText(_logMessage), top);
            SDL_RenderPresent(_renderer);
            return;
        }

        SDL_SetRenderScale(_renderer, _textScale, _textScale);
        var logicalWidth = width / _textScale;
        var baseline = height / _textScale - DebugFontSize - Margin;

        SDL_SetRenderDrawColor(_renderer, 105, 105, 105, 255);
        SDL_RenderDebugText(_renderer, Margin, baseline, Program.FormattedEditorVersion);
        SDL_RenderDebugText(_renderer, logicalWidth - Margin - _logMessage.Length * DebugFontSize, baseline, _logMessage);

        SDL_RenderPresent(_renderer);
    }

    private const float DebugFontSize = 8;
    private const float FontSize = 13;
    private const float Margin = 6;

    private SDL_Window* _window;
    private SDL_Renderer* _renderer;
    private SDL_Texture* _texture;
    private SDL_Texture* _fontTexture;
    private ImFontAtlasPtr _fontAtlas;
    private ImFontPtr _font;
    private Vector2 _fontAtlasSize;
    private float _textScale = 1;
    private float _pixelScale = 1;
    private int _mainThreadId = -1;
    private string _logMessage = "Loading TiXL...";
}

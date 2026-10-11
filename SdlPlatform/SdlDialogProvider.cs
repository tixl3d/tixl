using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using ImGuiNET;
using SDL;
using SilkWindows;
using T3.Core.Logging;
using T3.SystemUi;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>
/// Shows message boxes and other modal ImGui dialogs (the player's startup dialog) as SDL windows drawn with SDL's
/// 2D renderer. They share the main window's display, scaling and clipboard paths, and work before any GPU
/// renderer exists.
/// </summary>
/// <remarks>
/// Each dialog runs its own loop until its drawer asks to close or the window is closed, like a native modal
/// dialog. Events for other windows that arrive meanwhile are handed back to SDL afterwards rather than dropped.
/// </remarks>
public sealed unsafe class SdlDialogProvider : IMessageBoxProvider
{
    /// <summary>A Windows .ico for the dialogs' title bars, like the main window's. Without one they show the system default.</summary>
    public string? WindowIconPath { get; init; }

    /// <summary>
    /// Fonts for applications without an ImGui context of their own (the player), sized for 96 dpi and scaled to
    /// each dialog's display. Ignored once <see cref="ShareFonts"/> is called.
    /// </summary>
    public void SetFonts(FontPack fontPack) => _fontPack = fontPack;

    /// <summary>
    /// Lets dialogs use the host's font atlas, fonts and look (the editor's Markdown and buttons), so they read like
    /// the rest of the application. Dialogs shown before this is called use <see cref="SetFonts"/> or ImGui's
    /// built-in font.
    /// </summary>
    /// <remarks>The atlas is shared, not copied: font pointers the look holds stay valid in the dialog.</remarks>
    public void ShareFonts(ImFontAtlasPtr fontAtlas, ImFonts fonts, IMessageBoxLook? look)
    {
        _sharedFontAtlas = fontAtlas;
        _sharedFonts = fonts;
        _look = look;
    }

    public void ShowMessageBox(string message) => ShowMessageBox(message, "Notice");

    public void ShowMessageBox(string text, string title) => ShowMessageBox(text, title, str => str, "Ok");

    public T? ShowMessageBox<T>(string text, string title, Func<T, string>? toString, params T[]? buttons)
    {
        if (!CanShowWindows())
        {
            Log.Warning($"Message box '{title}' could not be shown here: {text}");
            return default;
        }

        return Show(title, new MessageBox<T>(text, buttons, toString, _sharedFonts != null ? _look : null));
    }

    /// <summary>Shows a modal dialog and returns its drawer's result: what the drawer chose, or its default when closed.</summary>
    /// <param name="options">Size (at 96 dpi) and behaviour. Its position, if given, picks the display to center on.</param>
    public TData? Show<TData>(string title, IImguiDrawer<TData> drawer, SimpleWindowOptions? options = null)
    {
        if (!CanShowWindows())
        {
            Log.Warning($"Dialog '{title}' could not be shown here.");
            return default;
        }

        Run(title, drawer, options);
        return drawer.Result;
    }

    /** SDL's video functions belong to the main thread, and need SDL video up. */
    private static bool CanShowWindows()
    {
        return SDL_IsMainThread() && (SDL_WasInit(SDL_InitFlags.SDL_INIT_VIDEO) & SDL_InitFlags.SDL_INIT_VIDEO) != 0;
    }

    private void Run(string title, IImguiDrawer drawer, SimpleWindowOptions? options)
    {
        var isResizable = options?.IsResizable ?? true;
        var size = options?.Size ?? new Vector2(DefaultWidth, DefaultHeight);

        var flags = SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY;
        if (isResizable)
            flags |= SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        if (options?.AlwaysOnTop ?? true)
            flags |= SDL_WindowFlags.SDL_WINDOW_ALWAYS_ON_TOP;

        var window = SDL_CreateWindow(title, (int)size.X, (int)size.Y, flags);
        if (window == null)
        {
            Log.Warning($"Could not open dialog '{title}': {SDL_GetError()}");
            drawer.OnClose();
            return;
        }

        if (WindowIconPath != null)
            SdlWindowIcon.TrySet(window, WindowIconPath);

        // Sized for 96 dpi like the splash screen: a mapped window knows both its display's scale and how many
        // pixels make a point, which sizes it alike on Windows (points are pixels) and Wayland (points are scaled).
        SDL_SyncWindow(window);
        var displayScale = SDL_GetWindowDisplayScale(window);
        var pixelDensity = SDL_GetWindowPixelDensity(window);
        if (displayScale <= 0)
            displayScale = 1;
        if (pixelDensity <= 0)
            pixelDensity = 1;

        var toPoints = displayScale / pixelDensity;
        SDL_SetWindowSize(window, (int)MathF.Round(size.X * toPoints), (int)MathF.Round(size.Y * toPoints));
        if (isResizable)
            SDL_SetWindowMinimumSize(window, (int)MathF.Round(MinimumWidth * toPoints), (int)MathF.Round(MinimumHeight * toPoints));

        var centered = (int)(SDL_WINDOWPOS_CENTERED_MASK | (uint)DisplayToCenterOn(options, size));
        SDL_SetWindowPosition(window, centered, centered);
        SDL_RaiseWindow(window);

        var renderer = SDL_CreateRenderer(window, (string?)null);
        if (renderer == null)
        {
            Log.Warning($"Could not draw dialog '{title}': {SDL_GetError()}");
            SDL_DestroyWindow(window);
            drawer.OnClose();
            return;
        }

        SDL_SetRenderVSync(renderer, 1);

        var sharesAtlas = _sharedFonts != null;
        var previousContext = ImGui.GetCurrentContext();
        var context = sharesAtlas ? ImGui.CreateContext(_sharedFontAtlas) : ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        SDL_Texture* fontTexture = null;
        try
        {
            var fonts = SetUpContext(previousContext, context, displayScale);
            var sharedAtlasTexture = sharesAtlas ? (IntPtr)ImGui.GetIO().Fonts.TexID : IntPtr.Zero;
            fontTexture = SdlImguiRenderer.CreateFontTexture(renderer, assignToAtlas: !sharesAtlas);
            drawer.Init();
            RunLoop(window, renderer, drawer, title, fonts, pixelDensity, sharedAtlasTexture, fontTexture);
            drawer.OnClose();
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previousContext);

            if (fontTexture != null)
                SDL_DestroyTexture(fontTexture);

            SDL_DestroyRenderer(renderer);
            SDL_DestroyWindow(window);
            RequeueForeignEvents();
        }
    }

    /** The display under the center of the requested position, or the primary one. */
    private static SDL_DisplayID DisplayToCenterOn(SimpleWindowOptions? options, Vector2 size)
    {
        if (options?.Position is not { } position)
            return SDL_GetPrimaryDisplay();

        var center = new SDL_Point { x = (int)(position.X + size.X / 2), y = (int)(position.Y + size.Y / 2) };
        var display = SDL_GetDisplayForPoint(&center);
        return display != 0 ? display : SDL_GetPrimaryDisplay();
    }

    /** Takes the host's style and fonts if there are any, so the dialog looks like the rest of the application. */
    private ImFonts SetUpContext(IntPtr previousContext, IntPtr context, float displayScale)
    {
        var io = ImGui.GetIO();
        io.NativePtr->IniFilename = null;
        SdlImguiClipboard.Install();

        if (previousContext != IntPtr.Zero)
        {
            ImGui.SetCurrentContext(previousContext);
            ImGuiStyle style = default;
            Unsafe.Copy(ref style, ImGui.GetStyle().NativePtr);
            ImGui.SetCurrentContext(context);
            Unsafe.Copy(ImGui.GetStyle().NativePtr, ref style);
        }
        else
        {
            ImGui.StyleColorsDark();
            ImGui.GetStyle().ScaleAllSizes(displayScale);
        }

        if (_sharedFonts != null)
            return _sharedFonts;

        var fontAtlas = io.Fonts;
        if (_fontPack is { } fontPack)
        {
            return new ImFonts([
                                   fontAtlas.AddFontFromFileTTF(fontPack.Small.Path, fontPack.Small.PixelSize * displayScale),
                                   fontAtlas.AddFontFromFileTTF(fontPack.Regular.Path, fontPack.Regular.PixelSize * displayScale),
                                   fontAtlas.AddFontFromFileTTF(fontPack.Bold.Path, fontPack.Bold.PixelSize * displayScale),
                                   fontAtlas.AddFontFromFileTTF(fontPack.Large.Path, fontPack.Large.PixelSize * displayScale),
                               ]);
        }

        var config = ImGuiNative.ImFontConfig_ImFontConfig();
        config->SizePixels = MathF.Round(13 * displayScale);
        fontAtlas.AddFontDefault(config);
        ImGuiNative.ImFontConfig_destroy(config);
        return new ImFonts([]);
    }

    private void RunLoop(SDL_Window* window, SDL_Renderer* renderer, IImguiDrawer drawer, string title, ImFonts fonts,
                         float pixelDensity, IntPtr sharedAtlasTexture, SDL_Texture* fontTexture)
    {
        var windowId = SDL_GetWindowID(window);
        var io = ImGui.GetIO();
        var clock = Stopwatch.StartNew();
        var mainWindowId = $"{title}##Dialog";

        while (true)
        {
            SDL_Event sdlEvent;
            while (SDL_PollEvent(&sdlEvent))
            {
                if (!IsFor(sdlEvent, windowId))
                {
                    KeepForeignEvent(sdlEvent);
                    continue;
                }

                if (sdlEvent.Type == SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED)
                    return;

                ForwardToImgui(io, sdlEvent, pixelDensity);
            }

            int width, height;
            SDL_GetWindowSizeInPixels(window, &width, &height);
            io.DisplaySize = new Vector2(width, height);
            io.DisplayFramebufferScale = Vector2.One;

            var deltaSeconds = Math.Max(clock.Elapsed.TotalSeconds, 1e-4);
            clock.Restart();
            io.DeltaTime = (float)deltaSeconds;

            // One panel colour edge to edge: the window is the panel, so ImGui's own window frame, rounding and
            // child backgrounds would only draw a box inside the box.
            var style = ImGui.GetStyle();
            var background = PanelColor(style);
            var padding = style.WindowPadding * 2;

            ImGui.NewFrame();
            ImGui.PushStyleColor(ImGuiCol.WindowBg, background);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(io.DisplaySize);
            ImGui.Begin(mainWindowId, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar
                                      | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse);
            drawer.OnRender(title, deltaSeconds, fonts);
            ImGui.End();
            ImGui.PopStyleVar(5);
            ImGui.PopStyleColor(2);
            ImGui.Render();

            SDL_SetRenderDrawColorFloat(renderer, background.X, background.Y, background.Z, 1);
            SDL_RenderClear(renderer);
            SdlImguiRenderer.Render(renderer, ImGui.GetDrawData(), sharedAtlasTexture, fontTexture);
            SDL_RenderPresent(renderer);

            drawer.OnWindowUpdate(deltaSeconds, out var shouldClose);
            if (shouldClose)
                return;
        }
    }

    private static void ForwardToImgui(ImGuiIOPtr io, in SDL_Event sdlEvent, float pixelDensity)
    {
        switch (sdlEvent.Type)
        {
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                io.AddMousePosEvent(sdlEvent.motion.x * pixelDensity, sdlEvent.motion.y * pixelDensity);
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                var button = sdlEvent.button.Button switch
                                 {
                                     SDLButton.SDL_BUTTON_LEFT   => 0,
                                     SDLButton.SDL_BUTTON_RIGHT  => 1,
                                     SDLButton.SDL_BUTTON_MIDDLE => 2,
                                     _                           => -1,
                                 };
                if (button >= 0)
                    io.AddMouseButtonEvent(button, sdlEvent.Type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN);
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                io.AddMouseWheelEvent(sdlEvent.wheel.x, sdlEvent.wheel.y);
                break;
            case SDL_EventType.SDL_EVENT_KEY_DOWN:
            case SDL_EventType.SDL_EVENT_KEY_UP:
                var key = ToImguiKey(sdlEvent.key.key);
                if (key != ImGuiKey.None)
                    io.AddKeyEvent(key, sdlEvent.Type == SDL_EventType.SDL_EVENT_KEY_DOWN);
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                io.AddFocusEvent(sdlEvent.Type == SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED);
                break;
        }
    }

    /** The keys a dialog answers to: confirming, cancelling and moving between its controls. */
    private static ImGuiKey ToImguiKey(SDL_Keycode keycode)
    {
        return keycode switch
                   {
                       SDL_Keycode.SDLK_ESCAPE    => ImGuiKey.Escape,
                       SDL_Keycode.SDLK_RETURN    => ImGuiKey.Enter,
                       SDL_Keycode.SDLK_KP_ENTER  => ImGuiKey.KeypadEnter,
                       SDL_Keycode.SDLK_TAB       => ImGuiKey.Tab,
                       SDL_Keycode.SDLK_SPACE     => ImGuiKey.Space,
                       SDL_Keycode.SDLK_UP        => ImGuiKey.UpArrow,
                       SDL_Keycode.SDLK_DOWN      => ImGuiKey.DownArrow,
                       SDL_Keycode.SDLK_LEFT      => ImGuiKey.LeftArrow,
                       SDL_Keycode.SDLK_RIGHT     => ImGuiKey.RightArrow,
                       SDL_Keycode.SDLK_BACKSPACE => ImGuiKey.Backspace,
                       _                          => ImGuiKey.None,
                   };
    }

    /** The theme's panel colour where it has one (child backgrounds), otherwise its window colour. */
    private static Vector4 PanelColor(ImGuiStylePtr style)
    {
        var child = style.Colors[(int)ImGuiCol.ChildBg];
        var color = child.W > 0.5f ? child : style.Colors[(int)ImGuiCol.WindowBg];
        return color with { W = 1 };
    }

    private static bool IsFor(in SDL_Event sdlEvent, SDL_WindowID windowId)
    {
        return sdlEvent.Type switch
                   {
                       SDL_EventType.SDL_EVENT_MOUSE_MOTION      => sdlEvent.motion.windowID == windowId,
                       SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN => sdlEvent.button.windowID == windowId,
                       SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP   => sdlEvent.button.windowID == windowId,
                       SDL_EventType.SDL_EVENT_MOUSE_WHEEL       => sdlEvent.wheel.windowID == windowId,
                       SDL_EventType.SDL_EVENT_KEY_DOWN          => sdlEvent.key.windowID == windowId,
                       SDL_EventType.SDL_EVENT_KEY_UP            => sdlEvent.key.windowID == windowId,
                       >= SDL_EventType.SDL_EVENT_WINDOW_FIRST and <= SDL_EventType.SDL_EVENT_WINDOW_LAST
                           => sdlEvent.window.windowID == windowId,
                       _ => false,
                   };
    }

    /** Mouse motion is left out: the host only needs where the pointer is now, not every step. */
    private void KeepForeignEvent(in SDL_Event sdlEvent)
    {
        if (sdlEvent.Type == SDL_EventType.SDL_EVENT_MOUSE_MOTION || _foreignEvents.Count >= MaxKeptForeignEvents)
            return;

        _foreignEvents.Add(sdlEvent);
    }

    private void RequeueForeignEvents()
    {
        foreach (var kept in _foreignEvents)
        {
            var copy = kept;
            SDL_PushEvent(&copy);
        }

        _foreignEvents.Clear();
    }

    private const int DefaultWidth = 624;
    private const int DefaultHeight = 360;
    private const int MinimumWidth = 320;
    private const int MinimumHeight = 240;
    private const int MaxKeptForeignEvents = 1024;

    private FontPack? _fontPack;
    private ImFontAtlasPtr _sharedFontAtlas;
    private ImFonts? _sharedFonts;
    private IMessageBoxLook? _look;
    private readonly List<SDL_Event> _foreignEvents = [];
}

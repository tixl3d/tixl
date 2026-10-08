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
/// Shows message boxes as SDL windows drawn with ImGui and SDL's 2D renderer. They share the main window's
/// display, scaling and clipboard paths, and work before the editor's own renderer exists.
/// </summary>
/// <remarks>
/// Each box runs its own loop until a button is chosen or the window is closed, like a native modal dialog.
/// Events for other windows that arrive meanwhile are handed back to SDL afterwards rather than dropped.
/// </remarks>
public sealed unsafe class SdlMessageBoxProvider : IMessageBoxProvider
{
    /// <summary>
    /// Lets boxes use the host's font atlas, fonts and look (the editor's Markdown and buttons), so they read like
    /// the rest of the application. Boxes shown before this is called use ImGui's built-in font and plain text.
    /// </summary>
    /// <remarks>The atlas is shared, not copied: font pointers the look holds stay valid in the box.</remarks>
    public void ShareFonts(ImFontAtlasPtr fontAtlas, ImFonts fonts, IMessageBoxLook? look)
    {
        _sharedFontAtlas = fontAtlas;
        _sharedFonts = fonts;
        _look = look;
    }

    /// <summary>A Windows .ico for the boxes' title bars, like the main window's. Without one they show the system default.</summary>
    public string? WindowIconPath { get; init; }

    public void ShowMessageBox(string message) => ShowMessageBox(message, "Notice");

    public void ShowMessageBox(string text, string title) => ShowMessageBox(text, title, str => str, "Ok");

    public T? ShowMessageBox<T>(string text, string title, Func<T, string>? toString, params T[]? buttons)
    {
        // SDL's video functions belong to the main thread, and need SDL video up; the text still reaches the log.
        if (!SDL_IsMainThread() || (SDL_WasInit(SDL_InitFlags.SDL_INIT_VIDEO) & SDL_InitFlags.SDL_INIT_VIDEO) == 0)
        {
            Log.Warning($"Message box '{title}' could not be shown here: {text}");
            return default;
        }

        var drawer = new MessageBox<T>(text, buttons, toString, _sharedFonts != null ? _look : null);
        return Run(title, drawer) ? drawer.Result : default;
    }

    /** True if the drawer produced a result, false if the window was closed or could not open. */
    private bool Run(string title, IImguiDrawer drawer)
    {
        var window = SDL_CreateWindow(title, DefaultWidth, DefaultHeight,
                                      SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY
                                      | SDL_WindowFlags.SDL_WINDOW_ALWAYS_ON_TOP);
        if (window == null)
        {
            Log.Warning($"Could not open message box '{title}': {SDL_GetError()}");
            return false;
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
        SDL_SetWindowSize(window, (int)MathF.Round(DefaultWidth * toPoints), (int)MathF.Round(DefaultHeight * toPoints));
        SDL_SetWindowMinimumSize(window, (int)MathF.Round(MinimumWidth * toPoints), (int)MathF.Round(MinimumHeight * toPoints));
        SDL_SetWindowPosition(window, (int)SDL_WINDOWPOS_CENTERED_MASK, (int)SDL_WINDOWPOS_CENTERED_MASK);
        SDL_RaiseWindow(window);

        var renderer = SDL_CreateRenderer(window, (string?)null);
        if (renderer == null)
        {
            Log.Warning($"Could not draw message box '{title}': {SDL_GetError()}");
            SDL_DestroyWindow(window);
            return false;
        }

        SDL_SetRenderVSync(renderer, 1);

        var sharesAtlas = _sharedFonts != null;
        var previousContext = ImGui.GetCurrentContext();
        var context = sharesAtlas ? ImGui.CreateContext(_sharedFontAtlas) : ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        SDL_Texture* fontTexture = null;
        var hasResult = false;
        try
        {
            var fonts = SetUpContext(previousContext, context, displayScale);
            var sharedAtlasTexture = sharesAtlas ? (IntPtr)ImGui.GetIO().Fonts.TexID : IntPtr.Zero;
            fontTexture = SdlImguiRenderer.CreateFontTexture(renderer, assignToAtlas: !sharesAtlas);
            drawer.Init();
            hasResult = RunLoop(window, renderer, drawer, title, fonts, pixelDensity, sharedAtlasTexture, fontTexture);
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

        return hasResult;
    }

    /** Takes the editor's style and fonts if there are any, so the box looks like the rest of TiXL. */
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

        var config = ImGuiNative.ImFontConfig_ImFontConfig();
        config->SizePixels = MathF.Round(13 * displayScale);
        io.Fonts.AddFontDefault(config);
        ImGuiNative.ImFontConfig_destroy(config);
        return new ImFonts([]);
    }

    private bool RunLoop(SDL_Window* window, SDL_Renderer* renderer, IImguiDrawer drawer, string title, ImFonts fonts,
                         float pixelDensity, IntPtr sharedAtlasTexture, SDL_Texture* fontTexture)
    {
        var windowId = SDL_GetWindowID(window);
        var io = ImGui.GetIO();
        var clock = Stopwatch.StartNew();
        var mainWindowId = $"{title}##MessageBox";

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

                switch (sdlEvent.Type)
                {
                    case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                        return false;
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
                    case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
                    case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                        io.AddFocusEvent(sdlEvent.Type == SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED);
                        break;
                }
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
                return true;
        }
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
                       >= SDL_EventType.SDL_EVENT_WINDOW_FIRST and <= SDL_EventType.SDL_EVENT_WINDOW_LAST
                           => sdlEvent.window.windowID == windowId,
                       _ => false,
                   };
    }

    /** Mouse motion is left out: the editor only needs where the pointer is now, not every step. */
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

    private ImFontAtlasPtr _sharedFontAtlas;
    private ImFonts? _sharedFonts;
    private IMessageBoxLook? _look;
    private readonly List<SDL_Event> _foreignEvents = [];
}

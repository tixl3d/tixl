#if !PLATFORM_WINDOWS
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using T3.Core.Logging;
using T3.Core.Operator.Slots;
using T3.Editor.App;
using T3.Editor.Gui;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.SystemUi;

namespace T3.Editor.UiContentDrawing;

/// <summary>
/// Linux ImGui content drawer using Silk.NET OpenGL + ImGui backend.
/// Replaces WindowsUiContentDrawer on non-Windows platforms.
/// </summary>
internal sealed class LinuxUiContentDrawer : IUiContentDrawer
{
    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _inputContext;
    private ImGuiController? _imguiController;
    private object _contextLock = new();

    /// <summary>
    /// Creates the Silk.NET window and initializes OpenGL + ImGui context.
    /// The window is created but NOT yet running its event loop.
    /// </summary>
    public void Initialize(string title, int width, int height, object imguiContextLock)
    {
        _contextLock = imguiContextLock;

        var options = WindowOptions.Default;
        options.Title = title;
        options.Size = new Vector2D<int>(width, height);
        options.API = GraphicsAPI.Default;
        options.VSync = true;
        options.WindowBorder = WindowBorder.Resizable;
        options.IsVisible = false; // hidden until fully initialized

        _window = Window.Create(options);

        // Initialize synchronously by running the window until Load fires
        _window.Load += () =>
        {
            _gl = _window.CreateOpenGL();
            _inputContext = _window.CreateInput();
            _imguiController = new ImGuiController(_gl, _window, _inputContext);

            ProgramWindows.Main.Width = _window.Size.X;
            ProgramWindows.Main.Height = _window.Size.Y;
        };

        _window.FramebufferResize += size =>
        {
            _gl?.Viewport(size);
            ProgramWindows.Main.Width = size.X;
            ProgramWindows.Main.Height = size.Y;
        };

        // Trigger the Load event by initializing the window
        _window.Initialize();
    }

    public unsafe bool CreateDeviceObjectsAndFonts()
    {
        if (_imguiController == null || _gl == null)
            return false;

        // On Linux we skip the custom icon atlas (uses WIC on Windows).
        // Set IconFont to a valid default font so Icons.DrawAtCursor() doesn't crash.
        var io = ImGui.GetIO();
        Icons.IconFont = io.Fonts.AddFontDefault();
        Icons.FontSize = 15;

        // Build the atlas (UiContentUpdate already added the TTF fonts)
        io.Fonts.Build();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

        // Upload font atlas to OpenGL texture
        if (_fontTexture != 0)
            _gl.DeleteTexture(_fontTexture);

        _fontTexture = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, _fontTexture);
        _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba, (uint)width, (uint)height, 0,
                        GLEnum.Rgba, GLEnum.UnsignedByte, (void*)pixels);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);

        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();

        return true;
    }

    private uint _fontTexture;

    public unsafe void InitializeScaling()
    {
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.NativePtr->IniFilename = null;
    }

    public void RenderCallback()
    {
        if (Program.IsShuttingDown)
            return;

        if (_gl == null || _imguiController == null)
            return;

        _gl.ClearColor(0.1f, 0.1f, 0.12f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        UiContentUpdate.SetupResourcesAndFontsWithScaling();
        UiContentUpdate.TakeMeasurement();

        ImGui.GetIO().DisplaySize = new System.Numerics.Vector2(
            ProgramWindows.Main.Width, ProgramWindows.Main.Height);

        DirtyFlag.IncrementGlobalTicks();

        // ImGuiController.Update() calls ImGui.NewFrame() internally
        _imguiController.Update((float)_lastDeltaTime);

        if (!string.IsNullOrEmpty(Program.NewImGuiLayoutDefinition))
        {
            ImGui.LoadIniSettingsFromMemory(Program.NewImGuiLayoutDefinition);
            Program.NewImGuiLayoutDefinition = string.Empty;
        }

        try
        {
            T3Ui.ProcessFrame();
        }
        catch (Exception e)
        {
            Log.Warning("Render frame error: " + e.Message);
        }

        // ImGuiController.Render() calls ImGui.Render() + draws via OpenGL
        _imguiController.Render();
    }

    /// <summary>
    /// Runs the Silk.NET window event loop. Blocks until the window is closed.
    /// </summary>
    public void RunMainLoop()
    {
        if (_window == null)
        {
            Log.Error("LinuxUiContentDrawer: Window not initialized");
            return;
        }

        _window.IsVisible = true;

        _window.Render += deltaTime =>
        {
            _lastDeltaTime = deltaTime;
            lock (_contextLock)
            {
                RenderCallback();
            }
        };

        _window.Run();
    }

    private double _lastDeltaTime;

    public void Dispose()
    {
        _imguiController?.Dispose();
        _inputContext?.Dispose();
        _gl?.Dispose();
        _window?.Dispose();
    }
}
#endif

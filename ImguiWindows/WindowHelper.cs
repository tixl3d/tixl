using System.Numerics;
using SilkWindows;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace SilkWindows;

public sealed class WindowHelper
{
    public static void RunWindow(IWindowImplementation window, IImguiDrawer drawer, FontPack? fontPack,
                                 object imguiContextLockObj)
    {
        var windowHelper = new WindowHelper(window, drawer, fontPack, imguiContextLockObj);
        windowHelper.RunUntilClosed();
    }
    
    private WindowHelper(IWindowImplementation window, IImguiDrawer drawer, FontPack? fontPack, object? graphicsContextLockObj)
    {
        _windowImpl = window;
        _drawer = drawer;
        _fontPack = fontPack;
        _graphicsContextLock = graphicsContextLockObj ?? new object();
    }
    
    public void RunUntilClosed()
    {
        var window = Window.Create(_windowImpl.WindowOptions);
        _window = window;
        SubscribeToWindow();
        _window.Run();
        UnsubscribeFromWindow();
        Dispose();
        
        return;
        
        void Dispose()
        {
            _windowImpl.Dispose();
            
            _graphicsContext?.Dispose();
            _inputContext?.Dispose();
            _window.Dispose();
            
            _graphicsContext = null;
            _inputContext = null;
        }
        
        void SubscribeToWindow()
        {
            _window.Load += OnLoad;
            _window.Render += RenderWindowContents;
            _window.FramebufferResize += OnWindowResize;
            _window.Update += OnWindowUpdate;
            _window.FocusChanged += OnFocusChanged;
            _window.Closing += OnClose;
            _window.FileDrop += OnFileDrop;
        }
        
        void UnsubscribeFromWindow()
        {
            _window.Load -= OnLoad;
            _window.Render -= RenderWindowContents;
            _window.FramebufferResize -= OnWindowResize;
            _window.Update -= OnWindowUpdate;
            _window.FocusChanged -= OnFocusChanged;
            _window.Closing -= OnClose;
            _window.FileDrop -= OnFileDrop;
        }
    }
    
    private void OnFileDrop(string[] filePaths)
    {
        _imguiHandler?.OnFileDrop(filePaths);
    }
    
    private void OnFocusChanged(bool isFocused)
    {
        if (!isFocused && _windowImpl.WindowOptions.TopMost)
        {
            // todo: force re-focus once silk.NET supports that ? wayland may not allow it anyway..
        }
        
        _imguiHandler?.OnWindowFocusChanged(isFocused);
    }
    
    private void RenderWindowContents(double deltaTime)
    {
        #if WH_DEBUG_FLOW
        Console.WriteLine("Starting render");
        #endif
        
        DebugMouse("RenderWindowContents");
        lock (_graphicsContextLock)
        {
            var windowSize = _window.Size;
            var clearColor = _imguiHandler?.ClearColor ?? _windowImpl.DefaultClearColor;
            
            if (_windowImpl.Render(clearColor, deltaTime))
            {
                _imguiHandler?.Draw(new Vector2(windowSize.X, windowSize.Y), deltaTime);
                _windowImpl.EndRender();
            }
        }
    }
    
    private void DebugMouse(string callsite)
    {
        // var mice = _inputContext!.Mice;
        // int mCounter = 0;
        // int wCounter = 0;
        // foreach(var mouse in mice)
        // {
        //     mCounter++;
        //     foreach (var wheel in mouse.ScrollWheels)
        //     {
        //         wCounter++;
        //         Console.WriteLine($"scroll in mouse {mouse.Name} ({mCounter}, {wCounter}) at {callsite}:" + wheel.Y);
        //     }
        //     
        //     wCounter = 0;
        // }
    }
    
    private void OnLoad()
    {
        _graphicsContext = _windowImpl.InitializeGraphicsAndInputContexts(_window, out _inputContext);
        
        if (_drawer == null)
            return;
        
        _imguiHandler = new ImGuiHandler(_windowImpl.GetImguiImplementation(), _drawer, _fontPack, _graphicsContextLock);
    }
    
    private void OnClose()
    {
        _imguiHandler?.Dispose();
    }
    
    private void OnWindowUpdate(double deltaSeconds)
    {
        DebugMouse("OnWindowUpdate");
        if (_imguiHandler == null) return;
        
        _imguiHandler.OnWindowUpdate(deltaSeconds, out var shouldCloseWindow);
        if (shouldCloseWindow)
        {
            _window.Close();
        }
    }
    
    private void OnWindowResize(Vector2D<int> size)
    {
        _windowImpl.OnWindowResize(size);
    }
    
    private readonly object _graphicsContextLock;
    private readonly IWindowImplementation _windowImpl;
    private IWindow _window = null!;
    private IInputContext? _inputContext;
    private NativeAPI? _graphicsContext;
    private readonly FontPack? _fontPack;
    
    private readonly IImguiDrawer? _drawer;
    private ImGuiHandler? _imguiHandler;
}
using System.Drawing;
using SilkWindows;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace SilkWindows.OpenGL;

// ReSharper disable once InconsistentNaming
internal sealed class GLWindow : IWindowImplementation
{
    // how do we dynamically bind the imgui stuff?
    public GLWindow(WindowOptions options, Action<GL>? renderContent = null)
    {
        var graphicsApi = options.API.API;
        if (graphicsApi != ContextAPI.OpenGL && graphicsApi != ContextAPI.OpenGLES)
        {
            Console.WriteLine("This handler is for OpenGL only");
            var api = GraphicsAPI.Default;
            options.API = api;
        }
        
        WindowOptions = options;
        _renderContent = renderContent;
    }
    
    public WindowOptions WindowOptions { get; }
    public Color DefaultClearColor => Color.Black;
    
    public bool Render(in Color clearColor, double deltaTime)
    {
        _graphicsContext!.ClearColor(clearColor);
        _graphicsContext.Clear(ClearBufferMask.ColorBufferBit);
        _renderContent?.Invoke(_graphicsContext);
        return true;
    }
    
    public void EndRender()
    {
    }
    
    public void Dispose()
    {
    }
    
    public NativeAPI? InitializeGraphicsAndInputContexts(IWindow window, out IInputContext inputContext)
    {
        _window = window;
        _graphicsContext = window.CreateOpenGL();
        inputContext = window.CreateInput();
        _inputContext = inputContext;
        return _graphicsContext;
    }
    
    public IImguiImplementation GetImguiImplementation()
    {
        return new GLImguiHandler(_window!.Title, _graphicsContext!, _window!, _inputContext!);
    }
    
    public void OnWindowResize(Vector2D<int> size)
    {
        _graphicsContext!.Viewport(size);
    }
    
    private GL? _graphicsContext;
    private IWindow? _window;
    private IInputContext? _inputContext;
    private readonly Action<GL>? _renderContent;
}
using SilkWindows;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace SilkWindows;

public sealed class GLImguiHandler : IImguiImplementation
{
    public GLImguiHandler(string title, GL graphicsContext, IWindow window, IInputContext inputContext)
    {
        Title = title;
        _graphicsContext = graphicsContext;
        _window = window;
        _inputContext = inputContext;
    }
    
    public void StartImguiFrame(float deltaSeconds)
    {
        _imguiController!.Update(deltaSeconds);
    }
    
    public void EndImguiFrame()
    {
        _imguiController!.Render();
    }
    
    public void Dispose()
    {
        _imguiController!.Dispose();
    }
    
    public string Title { get; }
    
    public IntPtr InitializeControllerContext(Action onConfigureIO)
    {
        _imguiController = new ImGuiController(gl: _graphicsContext, _window, _inputContext, null, onConfigureIO);
        return _imguiController.Context;
    }
    
    private ImGuiController? _imguiController;
    private readonly GL _graphicsContext;
    private readonly IWindow _window;
    private readonly IInputContext _inputContext;
}
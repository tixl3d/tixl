using System.Drawing;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace SilkWindows;

public interface IWindowImplementation
{
    public WindowOptions WindowOptions { get; }
    Color DefaultClearColor { get; }
    
    /// <summary>
    /// This function is called first and is used to set up your rendering context within your window, which was created
    /// with the <see cref="WindowOptions"/> property.
    /// </summary>
    /// <param name="window"></param>
    /// <returns></returns>
    NativeAPI? InitializeGraphicsAndInputContexts(IWindow window, out IInputContext inputContext);
    
    /// <summary>
    /// This is called first in the rendering process - used to clear the frame with a specific color and set up the frame.
    /// </summary>
    /// <param name="clearColor"></param>
    /// <param name="deltaTime">The time between frames</param>
    /// <returns>True if rendering should proceed</returns>
    public bool Render(in Color clearColor, double deltaTime);
    
    
    /// <summary>
    /// This is called after all draw commands have been issued, and is used to do the final rendering step of the Imgui frame.
    /// </summary>
    public void EndRender();
    
    /// <summary>
    /// Dispose of any resources that need to be disposed of.
    /// </summary>
    public void Dispose();
    
    public IImguiImplementation GetImguiImplementation();
    
    void OnWindowResize(Vector2D<int> size);
}

public interface IImguiImplementation
{
    public string Title { get; }
    
    /// <summary>
    /// Returns the imgui context pointer
    /// </summary>
    IntPtr InitializeControllerContext(Action onConfigureIO);
    
    public void StartImguiFrame(float deltaSeconds);
    public void EndImguiFrame();
    
    public void Dispose();
    
    public string MainWindowId => $"{Title}##{Interlocked.Increment(ref _windowCounter)}";
    public string ChildWindowId => $"{Title}##{Interlocked.Increment(ref _windowCounter)}";
    private static int _windowCounter = 999;
}
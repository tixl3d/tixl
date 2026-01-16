using T3.SystemUi;
using Silk.NET.Windowing;
using Silk.NET.Maths;
using SilkWindows.Implementations;
using SilkWindows.OpenGL;

namespace SilkWindows;

//https://github.com/dotnet/Silk.NET/blob/main/examples/CSharp/OpenGL%20Tutorials/Tutorial%201.1%20-%20Hello%20Window/Program.cs
public sealed class SilkWindowProvider : IImguiWindowProvider, IMessageBoxProvider
{
    public object ContextLock { get; } = new();
    
    public void SetFonts(FontPack fontPack)
    {
        _fontPack = fontPack;
    }
    
    public void ShowMessageBox(string message) => ShowMessageBox(message, "Notice");
    public void ShowMessageBox(string text, string title) => ShowMessageBox(text, title, str => str, "Ok");
    
    public T? ShowMessageBox<T>(string text, string title, Func<T, string>? toString, params T[]? buttons)
    {
        return Show(title, new MessageBox<T>(text, buttons, toString));
    }
    
    public TData? Show<TData>(string title, IImguiDrawer<TData> drawer, in SimpleWindowOptions? options = null)
    {
        var fullOptions = ConstructWindowOptions(options, title);
       // var window = new ImGuiVulkanWindowImpl(fullOptions);
       var window = new GLWindow(fullOptions);
        
        WindowHelper.RunWindow(window, drawer, _fontPack, ContextLock);
        return drawer.Result;
    }
    
    public async Task ShowAsync(string title, IImguiDrawer drawer, SimpleWindowOptions? options = null)
    {
        var windowTask = StartAsyncWindow(title, drawer, options, _fontPack);
        await windowTask;
    }
    
    // we can't simply return the result here, because nullable type constraints dont work between reference and value types
    public async Task ShowAsync<TData>(string title, AsyncImguiDrawer<TData> drawer, Action<TData> assign, SimpleWindowOptions? options = null)
    {
        var windowTask = StartAsyncWindow(title, drawer, options, _fontPack);
        
        await foreach (var result in drawer.GetResults())
        {
            if (result != null)
                assign(result);
        }
        
        await windowTask;
    }
    
    private async Task StartAsyncWindow(string title, IImguiDrawer drawer, SimpleWindowOptions? options, FontPack? fontPack)
    {
        var fullOptions = ConstructWindowOptions(options, title);
        
        var context = SynchronizationContext.Current;
        
        await Task.Run(() =>
                       {
                           //var window = new ImGuiVulkanWindowImpl(fullOptions);
                            var window = new GLWindow(fullOptions);
                           WindowHelper.RunWindow(window, drawer, fontPack, ContextLock);
                       }).ConfigureAwait(false);
        
        SynchronizationContext.SetSynchronizationContext(context);
        Console.WriteLine("Completed window run");
    }
    
    private static WindowOptions ConstructWindowOptions(in SimpleWindowOptions? options, string title)
    {
        var fullOptions = DefaultOptions;
        if (options.HasValue)
        {
            var val = options.Value;
            fullOptions.Size = val.Size.ToVector2DInt();
            fullOptions.FramesPerSecond = val.Fps;
            fullOptions.VSync = val.Vsync;
            fullOptions.WindowBorder = val.IsResizable ? WindowBorder.Resizable : WindowBorder.Fixed;
            fullOptions.TopMost = val.AlwaysOnTop;
        }
        
        fullOptions.Title = title;
        
        return fullOptions;
    }
    
    private FontPack? _fontPack;
    
    private static readonly WindowOptions DefaultOptions = new()
                                                               {
                                                                   API = GraphicsAPI.Default,
                                                                   IsEventDriven = false,
                                                                   ShouldSwapAutomatically = true,
                                                                   IsVisible = true,
                                                                   Position = new Vector2D<int>(600, 600),
                                                                   Size = new Vector2D<int>(400, 320),
                                                                   FramesPerSecond = 60,
                                                                   UpdatesPerSecond = 60,
                                                                   PreferredDepthBufferBits = 0,
                                                                   PreferredStencilBufferBits = 0,
                                                                   PreferredBitDepth = new Vector4D<int>(8, 8, 8, 8),
                                                                   Samples = 0,
                                                                   VSync = true,
                                                                   TopMost = true,
                                                                   WindowBorder = WindowBorder.Resizable
                                                               };
}

static class NoSynchronizationContextScope
{
    public static Disposable Enter()
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        return new Disposable(context);
    }
    
    public struct Disposable : IDisposable
    {
        private readonly SynchronizationContext _synchronizationContext;
        
        public Disposable(SynchronizationContext synchronizationContext)
        {
            _synchronizationContext = synchronizationContext;
        }
        
        public void Dispose() =>
            SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
    }
}
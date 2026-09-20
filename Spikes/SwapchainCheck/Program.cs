using System.Diagnostics;
using System.Numerics;
using SDL;
using T3.Graphics;
using T3.Graphics.Compat;
using T3.Graphics.Vulkan;
using static SDL.SDL3;
using Format = T3.Graphics.Format;

namespace T3.Spikes.SwapchainCheck;

/// <summary>
/// Presents to a real window through the compatibility layer on the Vulkan backend, the way the Player will:
/// acquire a back buffer, clear it, present, repeat. It runs for a fixed number of frames and reports what
/// the validation layer said, so it can be run without anyone watching the window.
/// </summary>
/// <remarks>
/// Run with a frame count: <c>dotnet run --project Spikes/SwapchainCheck -- 240</c>. It also resizes the
/// window halfway through, because recreating a swapchain mid-flight is where this usually breaks.
/// </remarks>
internal static unsafe class Program
{
    private static int Main(string[] args)
    {
        var frameCount = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 120;

        // 1 waits for a vertical blank, 0 presents as fast as the driver allows — a different present mode
        // on Vulkan, and the one a Player without vsync uses.
        var syncInterval = args.Length > 1 && int.TryParse(args[1], out var interval) ? interval : 1;

        GraphicsLog.Warning = message => Console.WriteLine($"[warn] {message}");
        GraphicsLog.Error = message => Console.WriteLine($"[error] {message}");

        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
        {
            Console.WriteLine($"SDL_Init failed: {SDL_GetError()}");
            return 1;
        }

        var window = SDL_CreateWindow("TiXL swapchain check"u8, Width, Height, SDL_WindowFlags.SDL_WINDOW_VULKAN
                                                                               | SDL_WindowFlags.SDL_WINDOW_RESIZABLE);

        if (window == null)
        {
            Console.WriteLine($"SDL_CreateWindow failed: {SDL_GetError()}");
            return 1;
        }

        // SDL knows which instance extensions this platform's surface needs; the backend cannot.
        uint extensionCount;
        var sdlExtensions = SDL_Vulkan_GetInstanceExtensions(&extensionCount);
        var instanceExtensions = new List<string>((int)extensionCount);

        for (var i = 0; i < extensionCount; i++)
        {
            instanceExtensions.Add(System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)sdlExtensions[i]) ?? string.Empty);
        }

        Console.WriteLine($"Instance extensions from SDL: {string.Join(", ", instanceExtensions)}");

        using var backend = new VulkanBackend(new VulkanBackendOptions
                                                  {
                                                      EnableValidation = true,
                                                      InstanceExtensions = instanceExtensions,
                                                  });
        Console.WriteLine($"GPU: {backend.AdapterName}");

        var device = new Device(backend);
        var context = device.ImmediateContext;

        var target = new SurfaceTarget
                         {
                             CreateVulkanSurface = instance =>
                                                   {
                                                       VkSurfaceKHR_T* surface = null;

                                                       if (SDL_Vulkan_CreateSurface(window, (VkInstance_T*)instance, null, &surface))
                                                           return (nint)surface;

                                                       Console.WriteLine($"SDL_Vulkan_CreateSurface failed: {SDL_GetError()}");
                                                       return nint.Zero;
                                                   },
                         };

        using var swapChain = SwapChain.TryCreate(device,
                                                  new SwapChainDescription
                                                      {
                                                          Width = Width,
                                                          Height = Height,
                                                          Format = Format.B8G8R8A8_UNorm,
                                                          BufferCount = 2,
                                                          UseFrameLatencyWaitableObject = true,
                                                      },
                                                  target, "window");

        if (swapChain == null)
        {
            Console.WriteLine("Could not create a swapchain.");
            return 1;
        }

        Console.WriteLine($"Swapchain: {swapChain.Width}x{swapChain.Height} {swapChain.Format}, sync interval {syncInterval}");

        var views = new Dictionary<Texture2D, RenderTargetView>();
        var stopwatch = Stopwatch.StartNew();
        var presented = 0;

        for (var frame = 0; frame < frameCount; frame++)
        {
            SDL_Event sdlEvent;

            while (SDL_PollEvent(&sdlEvent))
            {
                if (sdlEvent.Type == SDL_EventType.SDL_EVENT_QUIT)
                    frame = frameCount;
            }

            // Halfway through, resize: a swapchain that survives this survives a user dragging the window edge.
            if (frame == frameCount / 2)
            {
                foreach (var view in views.Values)
                {
                    view.Dispose();
                }

                views.Clear();
                SDL_SetWindowSize(window, Width + 160, Height + 120);
                swapChain.ResizeBuffers(Width + 160, Height + 120);
                Console.WriteLine($"Resized to {swapChain.Width}x{swapChain.Height}");
            }

            swapChain.WaitForFrameLatency();
            device.BeginFrame();

            var backBuffer = swapChain.GetBackBuffer();

            if (!views.TryGetValue(backBuffer, out var renderTarget))
            {
                renderTarget = new RenderTargetView(device, backBuffer);
                views[backBuffer] = renderTarget;
            }

            var phase = frame / (float)frameCount;
            context.OutputMerger.SetTargets(renderTarget);
            context.Rasterizer.SetViewport(0, 0, swapChain.Width, swapChain.Height);
            context.ClearRenderTargetView(renderTarget, new Vector4(phase, 0.2f, 1f - phase, 1f));

            swapChain.Present(syncInterval);
            device.EndFrame();
            presented++;
        }

        foreach (var view in views.Values)
        {
            view.Dispose();
        }

        var elapsed = stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine($"Presented {presented} frames in {elapsed:0.00} s ({presented / Math.Max(0.001, elapsed):0} fps)");
        Console.WriteLine($"Validation errors: {VulkanBackend.ValidationErrorCount}");

        SDL_DestroyWindow(window);
        SDL_Quit();

        return VulkanBackend.ValidationErrorCount == 0 ? 0 : 1;
    }

    private const int Width = 640;
    private const int Height = 360;
}

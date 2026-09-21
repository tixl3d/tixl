using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using T3.Graphics.Compat;
using T3.Graphics;
using T3.Core.IO;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Editor.Gui;
// for ReleaseMode
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.SdlPlatform;
using static SDL.SDL3;
using Device = T3.Graphics.Compat.Device;
using PixelShader = T3.Core.DataTypes.PixelShader;
using VertexShader = T3.Core.DataTypes.VertexShader;

namespace T3.Editor.App;

internal static unsafe class ProgramWindows
{
    public static AppWindow Main { get; private set; }
    public static AppWindow Viewer { get; private set; } // Required it distinguish 2nd render view in mouse handling   
    private static Device _device;
    private static DeviceContext _deviceContext;
    private static IGraphicsBackend? _backend;
    public static string ActiveGpu { get; private set; } = "Unknown";

    internal static void SetMainWindowSize(int width, int height)
    {
        Main.SetSize(width, height);
        Main.SetBorderStyleSizable();
    }

    /// <summary>
    /// Hands every Win32 message to the devices that read raw input from it, the SpaceMouse's WM_INPUT. SDL owns
    /// the window procedure, so they get the messages from its hook, before SDL dispatches them.
    /// </summary>
    internal static void SetInteractionDevices(params object[] objects)
    {
        _messageHandlers = objects.OfType<IWindowsFormsMessageHandler>().ToArray();
        if (OperatingSystem.IsWindows() && _messageHandlers.Length > 0)
            SDL_SetWindowsMessageHook(&ForwardWindowsMessage, IntPtr.Zero);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static SDLBool ForwardWindowsMessage(IntPtr userData, MSG* msg)
    {
        // Messages arrive before ImGui has a context, and the handlers read ImGui state.
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero)
            return true;

        // An exception must not unwind into SDL's native frame.
        try
        {
            var message = System.Windows.Forms.Message.Create(msg->hwnd, (int)msg->message, (nint)msg->wParam, msg->lParam);
            foreach (var handler in _messageHandlers)
            {
                handler.ProcessMessage(message);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Processing a window message failed: {e.Message}");
        }

        return true;
    }

    private static IWindowsFormsMessageHandler[] _messageHandlers = [];

    public static void SetVertexShader(Resource<VertexShader> resource) => _deviceContext.VertexShader.Set(resource.Value);
    public static void SetPixelShader(Resource<PixelShader> resource) => _deviceContext.PixelShader.Set(resource.Value);

    internal static void HandleFullscreenToggle()
    {
        if (Main.IsFullScreen == UserSettings.Config.FullScreen)
            return;

        if (UserSettings.Config.FullScreen)
        {
            // The display the window is already on: drag the editor where you want it, then go fullscreen —
            // which beats keeping a screen index that silently means a different monitor after a replug.
            Main.SetFullScreen(Main.GetDisplayIndex());
        }
        else
        {
            Main.SetSizeable();
        }
    }

    private sealed class  DisplayAdapterRating()
    {
        public string Name;
        public int Index;
        public float MemoryInGb =0;
        public float Rating = 1;
    }

    internal static void InitializeMainWindow(string version, out Device device)
    {
        Main = new AppWindow("TiXL " + version, disableClose: false);
        // Enable explicit frame-latency pacing on Main's swap chain. Replaces the accidental
        // pacing previously provided by Viewer's secondary `Present(1)` call. Must be set before
        // the swap chain is created.
        Main.UseFrameLatencyWaitable = true;
        device = null;

        try
        {
            _backend = CreateBackend();
            ActiveGpu = _backend.AdapterName;
            Log.Info($"Rendering with {_backend.GetType().Name} on {ActiveGpu}");

            device = new Device(_backend);
            _device = device;
            _deviceContext = device.ImmediateContext;

            Main.SetDevice(device, _deviceContext);
            Main.InitViewSwapChain();
            Main.InitializeWindow(maximized: Program.WindowSizeOverride == null, CanCloseMainWindow);
            if (Program.WindowSizeOverride is { } windowSize)
                Main.SetSize(windowSize.Width, windowSize.Height);

            if (OperatingSystem.IsWindows())
                IgnoreDxgiWindowShortcuts(device, Main.HwndHandle);
        }
        catch (Exception e)
        {
            if (e.Message.Contains("DXGI_ERROR_SDK_COMPONENT_MISSING"))
            {
                var result =
                    BlockingWindow.Instance
                                  .ShowMessageBox("You need to install the Windows Graphics Diagnostics Tools.\n\nClick OK to download this Windows component directly from Microsoft.",
                                                  "Windows Component Missing", "OK", "Cancel");
                if (result == "Ok")
                {
                    CoreUi.Instance
                          .OpenWithDefaultApplication("https://learn.microsoft.com/en-us/windows/uwp/gaming/use-the-directx-runtime-and-visual-studio-graphics-diagnostic-features");
                }
            }
            else
            {
                BlockingWindow.Instance.ShowMessageBox("We are sorry but your graphics hardware might not be capable of running TiXL\n\n" + e.Message,
                                                       "Oh noooo",
                                                       "Ok... /:");
            }

            Environment.Exit(0);
        }
    }

    /// <summary>
    /// Direct3D 11 on Windows, Vulkan everywhere else. The D3D11 path stays in its own method so its assemblies
    /// are only touched where they can load.
    /// </summary>
    private static IGraphicsBackend CreateBackend()
    {
        if (OperatingSystem.IsWindows())
            return CreateD3D11Backend();

        // TIXL_VULKAN_VALIDATION=1 turns the validation layer on. It reports invalid use while the command is
        // recorded, which is the only way to see what a driver later reports as nothing but a lost device.
        var validation = Environment.GetEnvironmentVariable("TIXL_VULKAN_VALIDATION") == "1";
        if (validation)
        {
            Log.Info("Vulkan validation layer enabled.");
            GraphicsLog.Error = message => Log.Error($"[vulkan] {message}");
            GraphicsLog.Warning = message => Log.Warning($"[vulkan] {message}");
        }

        return new T3.Graphics.Vulkan.VulkanBackend(new T3.Graphics.Vulkan.VulkanBackendOptions
                                                        {
                                                            EnableValidation = validation,
                                                            InstanceExtensions = SdlSurface.GetVulkanInstanceExtensions(),
                                                        });
    }

    /// <summary>Picks the adapter most likely to be the dedicated GPU, by name and memory.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IGraphicsBackend CreateD3D11Backend()
    {
        string[] highPerformanceKeywords = ["dedicated", "high performance", "rtx", "gtx"];
        string[] integratedKeywords = ["integrated", "intel(r) uhd graphics", "microsoft basic render", "microsoft basic render"]; // twice to make MS worse

        using var factory = new SharpDX.DXGI.Factory1();

        if (factory.GetAdapterCount() == 0)
        {
            BlockingWindow.Instance.ShowMessageBox("We are unable to find any graphics adapters",
                                                   "Oh noooo",
                                                   "OK");
            Environment.Exit(0);
        }

        var adapterRatings = new List<DisplayAdapterRating>(8);

        for (var i = 0; i < factory.GetAdapterCount(); i++)
        {
            using var adapter = factory.GetAdapter1(i);
            const long gb = 1024 * 1024 * 1024;
            
            var newRating = new DisplayAdapterRating
                                {
                                    Name = adapter.Description1.Description,
                                    Index = i,
                                    MemoryInGb = (float)((double)adapter.Description1.DedicatedVideoMemory/gb),
                                };
            adapterRatings.Add(newRating);                
            
            string descriptionLower = adapter.Description1.Description.ToLowerInvariant();
            
            // Positive keywords
            foreach (var keyword in highPerformanceKeywords)
            {
                if (!descriptionLower.Contains(keyword))
                    continue;

                newRating.Rating *= 2f;
            }

            // Negative keywords
            foreach (var keyword in integratedKeywords)
            {
                if (!descriptionLower.Contains(keyword))
                    continue;

                newRating.Rating *= 0.2f;
            }

            var memSizeFactor = newRating.MemoryInGb switch
                                    {
                                        < 1 => 0.1f,
                                        < 2 => 0.5f,
                                        < 4 => 1f,
                                        < 8 => 2f,
                                        > 8 => 3f,
                                        _ => 4f
                                    };
            newRating.Rating *= memSizeFactor;
        }

        var selectedAdapterIndex = adapterRatings.OrderByDescending(r => r.Rating).First().Index;
        Log.Debug("Detected display adapters...");
        foreach (var r in adapterRatings)
        {
            Log.Debug($"  #{r.Index}: {r.Name} / {r.MemoryInGb:0.0}GB  -> rated {r.Rating:0.0}");
        }
        
        using var selectedAdapter = factory.GetAdapter1(selectedAdapterIndex);

        var deviceCreationFlags = SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport;
        if (CoreSettings.Config.EnableDirectXDebug)
            deviceCreationFlags |= SharpDX.Direct3D11.DeviceCreationFlags.Debug;

        Log.Debug("Creating Device...");
        var nativeDevice = new SharpDX.Direct3D11.Device(selectedAdapter, deviceCreationFlags, SharpDX.Direct3D.FeatureLevel.Level_11_1);
        return new T3.Graphics.D3D11.D3D11Backend(nativeDevice, ownsDevice: true);
    }

    /// <summary>
    /// Stops DXGI from acting on the editor window's keys itself — Alt+Enter would switch to exclusive
    /// fullscreen behind the editor's back.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void IgnoreDxgiWindowShortcuts(Device device, IntPtr windowHandle)
    {
        using var nativeDevice = new SharpDX.Direct3D11.Device(device.NativePointer);
        Marshal.AddRef(device.NativePointer);
        using var dxgiDevice = nativeDevice.QueryInterface<SharpDX.DXGI.Device>();
        using var adapter = dxgiDevice.Adapter;
        using var factory = adapter.GetParent<SharpDX.DXGI.Factory>();
        factory.MakeWindowAssociation(windowHandle, SharpDX.DXGI.WindowAssociationFlags.IgnoreAll);
    }

    internal static void InitializeSecondaryViewerWindow(string name, int width, int height)
    {
        Viewer = CreateViewerWindow(name, width, height);
        Viewer.Show();
    }

    /// <summary>
    /// A window for showing a rendered texture: its own swap chain, no ImGui content and no key tracking, so it
    /// can sit on a projector while the editor keeps the keyboard.
    /// </summary>
    internal static AppWindow CreateViewerWindow(string name, int width, int height)
    {
        var window = new AppWindow(name, disableClose: true);
        window.SetDevice(_device, _deviceContext);
        window.SetSize(width, height);
        window.SetSizeable();
        window.InitViewSwapChain();
        window.InitializeWindow(maximized: false, canClose: null);
        return window;
    }

    private static bool CanCloseMainWindow()
    {
        if (EditableSymbolProject.AllProjects.Any(x => x.IsSaving))
        {
            Log.Debug($"Cancel closing because save-operation is in progress.");
            return false;
        }

#if DEBUG
        return true;
#else
        T3Ui.ExitDialog.ShowNextFrame();
        return false;
#endif
    }

    public static void Release()
    {
        OutputWindowHandling.Release();
        _device.ImmediateContext.ClearState();
        _deviceContext.Flush();
        Main.Release();
        Viewer?.Release();
        _backend?.Dispose();
    }

    public static void SetRasterizerState(RasterizerState viewWindowRasterizerState)
    {
        _deviceContext.Rasterizer.State = viewWindowRasterizerState;
    }

    public static void SetPixelShaderSRV(ShaderResourceView viewWindowBackgroundSrv)
    {
        _deviceContext.PixelShader.SetShaderResource(0, viewWindowBackgroundSrv);
    }

    public static void DrawTextureToSecondaryRenderOutput()
    {
        _deviceContext.Draw(3, 0);
        _deviceContext.PixelShader.SetShaderResource(0, null);
    }

    public static void RefreshViewport()
    {
        _deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, Main.Width, Main.Height, 0.0f, 1.0f));
        _deviceContext.OutputMerger.SetTargets(Main.RenderTargetView);
    }

    public static void Present(bool useVSync)
    {
        try
        {
            Main.SwapChain.Present(useVSync ? 1 : 0);

            // Always present the Viewer's swap chain, regardless of whether its window is shown.
            // Empirically, having two flip-model Present calls per frame in the same process
            // gives DWM's scheduler a noticeably better composition slot for Main — the Viewer's
            // Present acts as a co-pacing primitive even when its window is hidden. The cost of
            // the extra Present is small (the back buffer is unchanged when ShowSecondaryRenderWindow
            // is false; DWM doesn't display the hidden window; FlipDiscard discards the buffer
            // immediately on the next present cycle).
            Viewer?.SwapChain?.Present(useVSync ? 1 : 0);

            // Each display an output is bound to has its own swap chain, presented with the same sync as Main so
            // a projector never tears.
            var outputWindows = OutputWindowHandling.Presenting;
            for (var i = 0; i < outputWindows.Count; i++)
            {
                outputWindows[i].Window.SwapChain?.Present(useVSync ? 1 : 0);
            }
        }
        catch (SharpDX.SharpDXException e)
        {
            using var nativeDevice = new SharpDX.Direct3D11.Device(_device.NativePointer);
            System.Runtime.InteropServices.Marshal.AddRef(_device.NativePointer);
            var reason = nativeDevice.DeviceRemovedReason;
            string description;
            if (reason.Code == SharpDX.DXGI.ResultCode.DeviceHung.Code)
            {
                description = "device hung - the driver detected invalid GPU commands or a shader exceeded the TDR time limit";
            }
            else if (reason.Code == SharpDX.DXGI.ResultCode.DeviceReset.Code)
            {
                description = "device reset - the GPU was reset (e.g. driver update or TDR recovery)";
            }
            else if (reason.Code == SharpDX.DXGI.ResultCode.DeviceRemoved.Code)
            {
                description = "device removed - the GPU was physically removed, disabled, or the driver was upgraded";
            }
            else if (reason.Code == SharpDX.DXGI.ResultCode.DriverInternalError.Code)
            {
                description = "driver internal error";
            }
            else if (reason.Code == SharpDX.DXGI.ResultCode.InvalidCall.Code)
            {
                description = "invalid API call";
            }
            else if (reason.Code == SharpDX.Result.OutOfMemory.Code)
            {
                description = "out of GPU memory";
            }
            else
            {
                description = reason.ToString();
            }

            throw new ApplicationException($"Graphics device lost ({reason}: {description}): {e.Message}");
        }
    }

    /// <summary>
    /// Keeps a copy of the frame that is about to be presented. The flip-model swap chain discards
    /// its back buffer, so this copy is what <see cref="StallWatchdog"/> shows during a stall and
    /// what gets mirrored to the second view.
    /// </summary>
    public static void CaptureUiFrame()
    {
        lock (StallWatchdog.PresentLock)
        {
            RebuildUiCopyTextureIfRequired();
            _deviceContext.CopyResource(Main.BackBufferTexture, _uiCopyTexture);
        }
    }

    private static void RebuildUiCopyTextureIfRequired()
    {
        var backBufferDescription = Main.BackBufferTexture.Description;
        var needsRebuild = _uiCopyTexture == null ||
                           UiCopyTextureDescription.Width != backBufferDescription.Width ||
                           UiCopyTextureDescription.Height != backBufferDescription.Height;

        if (!needsRebuild)
            return;

        // Create a shader resource-compatible texture
        var textureDesc = new Texture2DDescription
        {
            Width = backBufferDescription.Width,
            Height = backBufferDescription.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Main.SwapChain.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        };

        if (_uiCopyTexture is { IsDisposed: false })
            _uiCopyTexture.Dispose();

        _uiCopyTexture = new Texture2D(_device, textureDesc);
        UiCopyTextureDescription = textureDesc;

        if (UiCopyTextureSrv is { IsDisposed: false })
            UiCopyTextureSrv.Dispose();

        UiCopyTextureSrv = new ShaderResourceView(_device, _uiCopyTexture);
    }

    /// <summary>
    /// For things like presentations, demos or certain live performance situations it
    /// can be desired to share also T3's UI content on a second display.
    ///  
    /// On Windows duplicating a display is extremely expensive. This work around
    /// copies the last frame into a texture which is then presented on the second display.
    /// </summary>
    public static void CopyUiContentToShareTexture()
    {
        if (_uiCopyTexture == null || _uiCopyTexture.IsDisposed)
        {
            Log.Warning("Can't use undefined uiCopyTexture");
            return;
        }

        _deviceContext.CopyResource(Main.BackBufferTexture, _uiCopyTexture);
    }

    /// <summary>
    /// Captures the main window's next rendered UI frame — the same back-buffer copy the second-display
    /// mirror makes — for the debug bridge. Served once by <see cref="ServePendingUiCapture"/>, after the
    /// UI has rendered and before Present. The receiver owns the texture it gets and disposes it when done.
    /// </summary>
    public static void RequestUiCapture(Action<T3.Core.DataTypes.Texture2D> onCaptured)
    {
        _pendingUiCapture = onCaptured;
    }

    internal static void ServePendingUiCapture()
    {
        var callback = _pendingUiCapture;
        if (callback == null)
            return;

        _pendingUiCapture = null;

        // A fresh copy per capture: the wrapper handed out disposes its native texture, so it must not be
        // the mirror's shared one.
        var swapChain = Main.SwapChain;
        var copy = new Texture2D(_device, new Texture2DDescription
                                              {
                                                  Width = swapChain.Width,
                                                  Height = swapChain.Height,
                                                  MipLevels = 1,
                                                  ArraySize = 1,
                                                  Format = swapChain.Format,
                                                  SampleDescription = new SampleDescription(1, 0),
                                                  Usage = ResourceUsage.Default,
                                                  BindFlags = BindFlags.ShaderResource,
                                                  CpuAccessFlags = CpuAccessFlags.None,
                                                  OptionFlags = ResourceOptionFlags.None,
                                              });
        _deviceContext.CopyResource(Main.BackBufferTexture, copy);
        callback(new T3.Core.DataTypes.Texture2D(copy));
    }

    private static Texture2D _uiCopyTexture;
    private static Action<T3.Core.DataTypes.Texture2D> _pendingUiCapture;
    public static ShaderResourceView UiCopyTextureSrv { get; private set; }

    internal static Texture2DDescription UiCopyTextureDescription { get; private set; }
}
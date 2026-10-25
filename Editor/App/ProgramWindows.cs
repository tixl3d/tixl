using System.Runtime.CompilerServices;
using System.Text;
using T3.Core.Diagnostics;
using System.Runtime.InteropServices;
using SDL;
using T3.Graphics.Compat;
using T3.Graphics;
using System.Diagnostics;
using System.Threading;
using T3.Core.Stats;
using T3.Core.IO;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Editor.Gui;
// for ReleaseMode
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Analyze;
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
        _messageHandlers = objects.OfType<IWin32MessageHandler>().ToArray();
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
            foreach (var handler in _messageHandlers)
            {
                handler.ProcessMessage((int)msg->message, (nint)msg->wParam, msg->lParam);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Processing a window message failed: {e.Message}");
        }

        return true;
    }

    private static IWin32MessageHandler[] _messageHandlers = [];

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
        // Main's swap chain paces the loop through its frame-latency waitable. Must be set before
        // the swap chain is created.
        Main.UseFrameLatencyWaitable = true;
        device = null;

        try
        {
            _backend = CreateBackend();
            ActiveGpu = _backend.AdapterName;
            EnvironmentReport.GraphicsDescription = _backend.BackendDescription;
            Log.Info($"Rendering with {_backend.BackendDescription}");
            LogEnvironment();

            device = new Device(_backend);
            _device = device;
            _deviceContext = device.ImmediateContext;

            Main.SetDevice(device, _deviceContext);
            Main.InitViewSwapChain();
            GpuFrameTimer.Initialize(device, _deviceContext);
            Main.InitializeWindow(maximized: Program.WindowSizeOverride == null, CanCloseMainWindow);
            if (Program.WindowSizeOverride is { } windowSize)
                Main.SetSize(windowSize.Width, windowSize.Height);

            // D3D11 only, not Windows only: this reaches through the device's native pointer as a
            // SharpDX D3D11 device, which a Vulkan device is not.
            if (OperatingSystem.IsWindows() && !UseVulkanBackend)
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
    /// Whether to create the Vulkan backend. It is the default everywhere and the only choice outside Windows;
    /// on Windows TIXL_BACKEND=d3d11 falls back to Direct3D 11, e.g. to compare output or to work around a
    /// Vulkan driver issue. Evaluated once - the backend cannot change while running.
    /// </summary>
    internal static bool UseVulkanBackend { get; } =
        !OperatingSystem.IsWindows()
        || !string.Equals(Environment.GetEnvironmentVariable("TIXL_BACKEND"), "d3d11", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Vulkan by default; Direct3D 11 on Windows when TIXL_BACKEND=d3d11. The D3D11 path stays in its own
    /// method so its assemblies are only touched where they can load.
    /// </summary>
    private static IGraphicsBackend CreateBackend()
    {
        // A backend that can't create a view or state reports it here and carries on, so without this the
        // operator just renders nothing.
        GraphicsLog.Error = message => Log.Error($"[graphics] {message}");
        GraphicsLog.Warning = message => Log.Warning($"[graphics] {message}");

        if (!UseVulkanBackend)
        {
            Log.Info("TIXL_BACKEND=d3d11: using the Direct3D 11 backend instead of Vulkan.");
            return CreateD3D11Backend();
        }

        // TIXL_VULKAN_VALIDATION=1 turns the validation layer on. It reports invalid use while the command is
        // recorded, which is the only way to see what a driver later reports as nothing but a lost device.
        var validation = Environment.GetEnvironmentVariable("TIXL_VULKAN_VALIDATION") == "1";
        if (validation)
        {
            Log.Info("Vulkan validation layer enabled.");
            GraphicsLog.Debug = message => Log.Debug($"[graphics] {message}");
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

    /// <summary>
    /// A window for showing a rendered texture: its own swap chain, no ImGui content and no key tracking, so it
    /// can sit on a projector while the editor keeps the keyboard.
    /// </summary>
    internal static AppWindow CreateOutputWindow(string name, int width, int height)
    {
        var window = new AppWindow(name, disableClose: true);
        window.SetDevice(_device, _deviceContext);
        window.SetSize(width, height);
        window.SetSizeable();
        window.IsPacedByMainWindow = true;
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
        GpuFrameTimer.Release();
        _device.ImmediateContext.ClearState();
        _deviceContext.Flush();
        Main.Release();
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

    public static void DrawFullScreenTexture()
    {
        _deviceContext.Draw(3, 0);
        _deviceContext.PixelShader.SetShaderResource(0, null);
    }

    public static void RefreshViewport()
    {
        _deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, Main.Width, Main.Height, 0.0f, 1.0f));
        _deviceContext.OutputMerger.SetTargets(Main.RenderTargetView);
    }

    /// <summary>
    /// Holds the loop until the previous frame has lasted the whole refreshes the frame-rate setting asks for. The
    /// swap chain paces to single vblanks only, so lower rates come from waiting out the extra ones here, and
    /// <see cref="FrameTiming.VblanksPerFrame"/> keeps animation speed unchanged. Call once per frame, right after
    /// the frame-latency wait and before input is read.
    /// </summary>
    public static void HoldForTargetFrameRate()
    {
        var vblanksPerFrame = T3Ui.UseVSync ? GetVblanksPerFrame(UserSettings.Config.TargetFrameRate, FrameTiming.RefreshRate) : 1;
        FrameTiming.VblanksPerFrame = vblanksPerFrame;

        if (vblanksPerFrame > 1)
        {
            // Released a quarter period early: the present still lands on the intended vblank, and a frame whose
            // work fills a whole period keeps its head start.
            var minFrameTicks = (long)((vblanksPerFrame - VblankTimingTolerance) * FrameTiming.RefreshPeriodSec * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() - _lastPacedFrameStart < minFrameTicks)
            {
                Thread.Sleep(1);
            }
        }

        _lastPacedFrameStart = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// How many vblanks each frame stays on screen to approach the target frame rate on the pacing display.
    /// Only whole vblanks are possible, so 50 fps on a 120 Hz display gives 60 fps.
    /// </summary>
    private static int GetVblanksPerFrame(int targetFps, double refreshRate)
    {
        if (targetFps <= 0)
            return 1;

        return Math.Clamp((int)Math.Round(refreshRate / targetFps), 1, MaxVblanksPerFrame);
    }

    private const double VblankTimingTolerance = 0.25;

    /** 30 fps on a 120 Hz display; lower rates don't need a frame-rate setting. */
    private const int MaxVblanksPerFrame = 4;
    private static long _lastPacedFrameStart;

    public static void Present(bool useVSync)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        GpuFrameTimer.EndFrame();
        try
        {
            Main.SwapChain.Present(useVSync ? 1 : 0);
            PresentationDiagnostics.Update(Main.SwapChain);

            // Each display an output is bound to has its own swap chain, presented with the same sync as Main so
            // a projector never tears. Only claimed windows are presented: DWM throttles presents to a hidden
            // flip-model swap chain, which measured 34ms per frame against 0.3ms for Main.
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
        finally
        {
            // Without vsync this is where a GPU that can't keep up shows itself: the driver blocks the queue
            // here rather than in the work that filled it.
            PerformanceMetrics.RecordPresent((float)((Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency));
        }
    }

    /// <summary>
    /// Keeps a copy of the frame that is about to be presented. The flip-model swap chain discards
    /// its back buffer, so this copy is what <see cref="StallWatchdog"/> shows during a stall and what the
    /// output setup maps as the editor UI.
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

        // The wrapper owns the native texture, so disposing it frees both.
        UiFrame?.Dispose();

        _uiCopyTexture = new Texture2D(_device, textureDesc);
        UiFrame = new T3.Core.DataTypes.Texture2D(_uiCopyTexture);
        UiCopyTextureDescription = textureDesc;

        if (UiCopyTextureSrv is { IsDisposed: false })
            UiCopyTextureSrv.Dispose();

        UiCopyTextureSrv = new ShaderResourceView(_device, _uiCopyTexture);
    }

    /// <summary>
    /// Captures the main window's next rendered UI frame for the debug bridge. Served once by
    /// <see cref="ServePendingUiCapture"/>, after the UI has rendered and before Present. The receiver owns the
    /// texture it gets and disposes it when done.
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
        // the stall watchdog's kept frame.
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

    /// <summary>The last presented UI frame, kept by <see cref="CaptureUiFrame"/>; null until the first one.</summary>
    public static T3.Core.DataTypes.Texture2D UiFrame { get; private set; }

    internal static Texture2DDescription UiCopyTextureDescription { get; private set; }

    /// <summary>
    /// Records what this machine is, once, so a pasted log answers the questions a Linux bug report always
    /// raises - which distribution, which session, which driver - without asking the reporter.
    /// </summary>
    private static void LogEnvironment()
    {
        var builder = new StringBuilder("Environment:");
        foreach (var (key, value) in EnvironmentReport.Collect())
        {
            builder.Append("\n  ").Append(key).Append(": ").Append(value);
        }

        Log.Debug(builder.ToString());
    }

}

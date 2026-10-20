using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using System.Diagnostics;
using SharpDX.DXGI;
using T3.Core.Logging;
using T3.Core.Stats;
using T3.Core.IO;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Editor.Gui;
// for ReleaseMode
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Analyze;
using T3.Editor.UiModel;
using Device = SharpDX.Direct3D11.Device;
using PixelShader = T3.Core.DataTypes.PixelShader;
using VertexShader = T3.Core.DataTypes.VertexShader;

namespace T3.Editor.App;

internal static class ProgramWindows
{
    public static AppWindow Main { get; private set; }
    public static AppWindow Viewer { get; private set; } // Required it distinguish 2nd render view in mouse handling   
    private static Device _device;
    private static DeviceContext _deviceContext;
    private static Factory _factory;

    /** The window whose display's vblank paces the frame loop. */
    private static AppWindow _pacingWindow;

    private static int _pacingVblanksPerFrame = 1;
    private static bool _isPacingWithVSync = true;
    private static long _lastPacedFrameStart;
    private static bool _isWaitingForVblanks;
    private static int _pacingFrameLatency = 1;

    /**
     * Fraction of a refresh period a frame may start early without waiting for another vblank. Large enough for
     * start jitter, small enough that a frame released half a period early (120 Hz clock, 60 Hz display) still waits.
     */
    private const double VblankTimingTolerance = 0.25;

    /** The display the pacing window is on, for waiting on its vblanks; refreshed when the pacing window changes. */
    private static Output _pacingOutput;
    private static AppWindow _pacingOutputWindow;
    private static long _pacingOutputLookupTimestamp;
    private static bool _hasLoggedVblankWaitFailure;

    /** 30 fps on a 120 Hz display; lower rates don't need a frame-rate setting. */
    private const int MaxVblanksPerFrame = 4;

    public static string ActiveGpu { get; private set; } = "Unknown";

    internal static void SetMainWindowSize(int width, int height)
    {
        Main.SetSize(width, height);
        Main.SetBorderStyleSizable();
    }

    internal static void SetInteractionDevices(params object[] objects)
    {
        IWindowsFormsMessageHandler[] messageHandlers = objects.OfType<IWindowsFormsMessageHandler>().ToArray();
        ImGuiDx11RenderForm.InputMethods = messageHandlers;
    }

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
            Main.SetFullScreen(IndexOfScreenUnder(Main));
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
        // SwapChainDescription is consumed by Device.CreateWithSwapChain.
        Main.UseFrameLatencyWaitable = true;
        _pacingWindow = Main;
        device = null;
        string[] highPerformanceKeywords = ["dedicated", "high performance", "rtx", "gtx"];
        string[] integratedKeywords = ["integrated", "intel(r) uhd graphics", "microsoft basic render", "microsoft basic render"]; // twice to make MS worse

        try
        {
            using var factory = new Factory1();

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
                                        Name = adapter.Description.Description,
                                        Index = i,
                                        MemoryInGb = (float)((double)adapter.Description.DedicatedVideoMemory/gb),
                                    };
                adapterRatings.Add(newRating);                
                
                var descriptionLower = adapter.Description.Description.ToLowerInvariant();
                
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
            
            var selectedAdapter = factory.GetAdapter1(selectedAdapterIndex);
            ActiveGpu = selectedAdapter.Description.Description;

            //Try to load 11.1 if possible, revert to 11.0 auto
            FeatureLevel[] levels =
            [
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
            ];

            
            
            // Create Device and SwapChain with the selected adapter
            var deviceCreationFlags = DeviceCreationFlags.BgraSupport;

            if (CoreSettings.Config.EnableDirectXDebug)
                deviceCreationFlags |= DeviceCreationFlags.Debug;
            
            Log.Debug("Creating Device...");

            SwapChain swapchain;
            try
            {
                Device.CreateWithSwapChain(selectedAdapter,
                                           deviceCreationFlags,
                                           levels,
                                           Main.SwapChainDescription,
                                           out device,
                                           out  swapchain);
            }
            catch (Exception e)
            {
                Log.Warning("Failed to create device with advanced features. Trying basic settings. " + e.Message);
                Device.CreateWithSwapChain(selectedAdapter,
                                           DeviceCreationFlags.None,
                                           levels,
                                           Main.SwapChainDescription,
                                           out device,
                                           out  swapchain);
            }

            _device = device;
            _deviceContext = device.ImmediateContext;
            _factory = swapchain.GetParent<Factory>();

            Main.SetDevice(device, _deviceContext, swapchain);
            GpuFrameTimer.Initialize(device, _deviceContext);
            var windowState = Program.WindowSizeOverride == null ? FormWindowState.Maximized : FormWindowState.Normal;
            Main.InitializeWindow(windowState, OnCloseMainWindow, true);
            if (Program.WindowSizeOverride is { } windowSize)
                Main.SetSize(windowSize.Width, windowSize.Height);
            _factory.MakeWindowAssociation(Main.HwndHandle, WindowAssociationFlags.IgnoreAll);
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

    internal static void InitializeSecondaryViewerWindow(string name, int width, int height)
    {
        Viewer = CreateViewerWindow(name, width, height);
        Viewer.Show();
    }

    /// <summary>The display a window sits on, by its index in the arrangement; 0 when it can't be placed.</summary>
    private static int IndexOfScreenUnder(AppWindow window)
    {
        var screen = Screen.FromControl(window.Form);
        var screens = Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            if (screens[i].DeviceName == screen.DeviceName)
                return i;
        }

        return 0;
    }

    /// <summary>
    /// A window for showing a rendered texture: its own swap chain, no ImGui content and no key tracking, so it
    /// can sit on a projector while the editor keeps the keyboard.
    /// </summary>
    internal static AppWindow CreateViewerWindow(string name, int width, int height)
    {
        var window = new AppWindow(name, disableClose: true);

        // An output window paces the frame loop while an output is bound to its display.
        window.UseFrameLatencyWaitable = true;
        window.SetDevice(_device, _deviceContext);
        window.SetSize(width, height);
        window.SetSizeable();
        window.InitViewSwapChain(_factory);
        window.InitializeWindow(FormWindowState.Normal, null, false);
        return window;
    }

    private static void OnCloseMainWindow(object sender, FormClosingEventArgs args)
    {
        if (EditableSymbolProject.AllProjects.Any(x => x.IsSaving))
        {
            args.Cancel = true;
            Log.Debug($"Cancel closing because save-operation is in progress.");
        }
        else
        {
#if DEBUG
            args.Cancel = false;
#else
            args.Cancel = true;
            T3Ui.ExitDialog.ShowNextFrame();
#endif
        }
    }

    public static void Release()
    {
        OutputWindowHandling.Release();
        GpuFrameTimer.Release();
        Main.Release();
        Viewer.Release();
        _device.ImmediateContext.ClearState();
        _deviceContext.Flush();
        _device.Dispose();
        _deviceContext.Dispose();
        _factory.Dispose();
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

    /// <summary>
    /// Blocks until the pacing window's swap chain accepts the next frame. Call once at the start of each frame.
    /// </summary>
    public static void WaitForFrameLatency()
    {
        _pacingWindow.WaitForFrameLatency();

        // The latency waitable releases the loop once per vblank of the primary display's clock while DWM composes,
        // whatever the sync interval. A window on another display, or a frame that should last several vblanks,
        // therefore waits for the pacing display's own vblanks until the frame has lasted long enough. On the primary
        // display at full rate the waitable alone paces, and the extra wait must stay out of the way: it fires
        // whenever the waitable released the loop a little before the vblank, and that head start is what a frame
        // whose CPU and GPU work add up to a full period needs to make its vblank.
        // With a frame latency of 2 the loop paces like the Vulkan backend: the waitable alone, a frame in flight
        // ahead. Waiting for the secondary display's vblanks on top would take that head start away again.
        var needsVblankWait = _isPacingWithVSync
                              && (_pacingVblanksPerFrame > 1
                                  || (_pacingFrameLatency == 1 && !PresentationDiagnostics.IsPacingWindowOnPrimaryDisplay));
        if (needsVblankWait != _isWaitingForVblanks)
        {
            _isWaitingForVblanks = needsVblankWait;
            Log.Debug(needsVblankWait
                          ? $"Frame pacing waits for the pacing display's vblanks ({_pacingVblanksPerFrame} per frame, "
                            + $"window on primary display: {PresentationDiagnostics.IsPacingWindowOnPrimaryDisplay})"
                          : "Frame pacing follows the frame-latency waitable alone");
        }

        if (needsVblankWait)
        {
            var minFrameTicks = (long)((_pacingVblanksPerFrame - VblankTimingTolerance) * FrameTiming.RefreshPeriodSec * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() - _lastPacedFrameStart < minFrameTicks
                   && WaitForVerticalBlank(_pacingWindow))
            {
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

    /// <summary>
    /// Presents every shown window. Only the pacing window may block on its display's vblank: windows on other
    /// displays run on their own refresh clocks, and waiting for two clocks in one loop gives neither an even
    /// cadence. They present without waiting and drop the frame when their display is still busy.
    /// </summary>
    public static void Present(bool useVSync)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        GpuFrameTimer.EndFrame();
        try
        {
            var syncInterval = useVSync ? 1 : 0;

            // The projector matters more than the editor UI during a show, so the first bound output paces.
            var outputWindows = OutputWindowHandling.Presenting;
            var pacingWindow = outputWindows.Count > 0 ? outputWindows[0].Window : Main;
            if (pacingWindow != _pacingWindow)
            {
                Log.Debug($"Frame pacing now follows '{pacingWindow.Form.Text}'");
                _pacingWindow = pacingWindow;
            }

            // Takes effect in the next frame's wait; windows on other displays just show each frame longer.
            var vblanksPerFrame = useVSync ? GetVblanksPerFrame(UserSettings.Config.TargetFrameRate, FrameTiming.RefreshRate) : 1;
            FrameTiming.VblanksPerFrame = vblanksPerFrame;
            _pacingVblanksPerFrame = vblanksPerFrame;
            _isPacingWithVSync = useVSync;

            _pacingFrameLatency = Math.Clamp(UserSettings.Config.FrameLatency, 1, 2);
            Main.SetMaximumFrameLatency(_pacingFrameLatency);
            for (var i = 0; i < outputWindows.Count; i++)
            {
                outputWindows[i].Window.SetMaximumFrameLatency(_pacingFrameLatency);
            }

            PresentWindow(Main, syncInterval);
            PresentationDiagnostics.Update(Main.SwapChain);
            PresentationDiagnostics.MeasureRefreshPeriod(_pacingWindow.SwapChain, _pacingWindow.HwndHandle);

            // Only while its window is actually shown. Presenting a hidden flip-model swap chain is not the
            // cheap no-op it looks like: DWM throttles presents to a window it isn't displaying, and with an
            // output window up that wait measured 34ms per frame against 0.3ms for Main.
            if (T3Ui.ShowSecondaryRenderWindow && Viewer != null)
                PresentWindow(Viewer, syncInterval);

            // Synced even when not pacing, so a projector never tears.
            for (var i = 0; i < outputWindows.Count; i++)
            {
                PresentWindow(outputWindows[i].Window, syncInterval);
            }
        }
        catch (SharpDX.SharpDXException e)
        {
            var reason = _device.DeviceRemovedReason;
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
            else if (reason.Code == Result.OutOfMemory.Code)
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

    private static void PresentWindow(AppWindow window, int syncInterval)
    {
        var swapChain = window.SwapChain;
        if (swapChain == null)
            return;

        if (window == _pacingWindow)
        {
            swapChain.Present(syncInterval, PresentFlags.None);
            return;
        }

        var result = swapChain.TryPresent(syncInterval, PresentFlags.DoNotWait);
        if (result == SharpDX.DXGI.ResultCode.WasStillDrawing.Result)
            return;

        result.CheckError();
    }

    /** False when the display couldn't be waited on, so callers don't spin. */
    private static bool WaitForVerticalBlank(AppWindow window)
    {
        try
        {
            // Looked up again now and then: the window can be moved to another display.
            var now = Stopwatch.GetTimestamp();
            if (_pacingOutputWindow != window || _pacingOutput == null || now - _pacingOutputLookupTimestamp > Stopwatch.Frequency)
            {
                _pacingOutputLookupTimestamp = now;
                _pacingOutput?.Dispose();
                _pacingOutput = window.SwapChain.ContainingOutput;
                _pacingOutputWindow = window;
            }

            _pacingOutput.WaitForVerticalBlank();
            _hasLoggedVblankWaitFailure = false;
            return true;
        }
        catch (SharpDX.SharpDXException e)
        {
            // A window moved between displays or a display reconfiguration; look the output up again next frame.
            if (!_hasLoggedVblankWaitFailure)
                Log.Debug($"Waiting for the pacing display's vblank failed: {e.Message}");

            _hasLoggedVblankWaitFailure = true;
            _pacingOutput?.Dispose();
            _pacingOutput = null;
            return false;
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
            Format = Main.SwapChain.Description.ModeDescription.Format,
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
        var mode = Main.SwapChain.Description.ModeDescription;
        var copy = new Texture2D(_device, new Texture2DDescription
                                              {
                                                  Width = mode.Width,
                                                  Height = mode.Height,
                                                  MipLevels = 1,
                                                  ArraySize = 1,
                                                  Format = mode.Format,
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
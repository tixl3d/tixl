using System.ComponentModel;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.IO;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Editor.Gui;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using Device = SharpDX.Direct3D11.Device;
using PixelShader = T3.Core.DataTypes.PixelShader;
using VertexShader = T3.Core.DataTypes.VertexShader;
using Vector4 = System.Numerics.Vector4;

namespace T3.Editor.App;

internal static class ProgramWindows
{
    public static AppWindow Main { get; private set; }
    public static AppWindow Viewer { get; private set; } // Required it distinguish 2nd render view in mouse handling   
    private static Device _device;
    private static DeviceContext _deviceContext;
    private static Factory _factory;
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

        var screenCount = Screen.AllScreens.Length;
        if (UserSettings.Config.FullScreen)
        {
            Main.SetFullScreen(UserSettings.Config.FullScreenIndexMain < screenCount ? UserSettings.Config.FullScreenIndexMain : 0);
        }
        else
        {
            Main.SetSizeable();
        }
    }

    /// <summary>
    /// Updates the viewer window spanning bounds dynamically
    /// Called whenever the spanning area selection changes in the Screen Manager
    /// </summary>
    internal static void UpdateViewerSpanning(Vector4 spanningBounds)
    {
        if (Viewer == null)
            return;

        // Check if there's a valid spanning area defined
        if (spanningBounds.Z > 0 && spanningBounds.W > 0)
        {
            // Update the viewer window to the spanning bounds
            Viewer.UpdateSpanningBounds(
                (int)spanningBounds.X,
                (int)spanningBounds.Y,
                (int)spanningBounds.Z,
                (int)spanningBounds.W
            );
        }
    
    }

    /// <summary>
    /// Call this when the secondary render window is enabled/disabled
    /// to ensure the viewer window is properly configured
    /// </summary>
    internal static void UpdateViewerWindowState()
    {
        if (Viewer == null)
            return;

        var spanningBounds = UserSettings.Config.OutputArea;

        // If there's a valid spanning area, apply it
        if (spanningBounds.Z > 0 && spanningBounds.W > 0)
        {
            UpdateViewerSpanning(spanningBounds);
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

            if (ProjectSettings.Config.EnableDirectXDebug)
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
            Main.InitializeWindow(FormWindowState.Maximized, OnCloseMainWindow, true);
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
        Viewer = new(name, disableClose: true);
        Viewer.SetDevice(_device, _deviceContext);
        Viewer.SetSize(width, height);
        Viewer.SetSizeable();
        Viewer.InitViewSwapChain(_factory);
        Viewer.InitializeWindow(FormWindowState.Normal, null, false);
        Viewer.Show();
    }

    private static void OnCloseMainWindow(object sender, CancelEventArgs args)
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

    public static void Present(bool useVSync, bool showSecondaryRenderWindow)
    {
        try
        {
            Main.SwapChain.Present(useVSync ? 1 : 0, PresentFlags.None);

            if (showSecondaryRenderWindow)
                Viewer.SwapChain.Present(useVSync ? 1 : 0, PresentFlags.None);
        }
        catch (SharpDX.SharpDXException e)
        {
            string description;
            var result = _device.DeviceRemovedReason;
            if (result == Result.Abort)
            {
                description = Result.Abort.Description;
            }
            else if (result == Result.AccessDenied)
            {
                description = Result.AccessDenied.Description;
            }
            else if (result == Result.Fail)
            {
                description = Result.Fail.Description;
            }
            else if (result == Result.Handle)
            {
                description = Result.Handle.Description;
            }
            else if (result == Result.InvalidArg)
            {
                description = Result.InvalidArg.Description;
            }
            else if (result == Result.NoInterface)
            {
                description = Result.NoInterface.Description;
            }
            else if (result == Result.NotImplemented)
            {
                description = Result.NotImplemented.Description;
            }
            else if (result == Result.OutOfMemory)
            {
                description = Result.OutOfMemory.Description;
            }
            else if (result == Result.InvalidPointer)
            {
                description = Result.InvalidPointer.Description;
            }
            else if (result == Result.UnexpectedFailure)
            {
                description = Result.UnexpectedFailure.Description;
            }
            else if (result == Result.WaitAbandoned)
            {
                description = Result.WaitAbandoned.Description;
            }
            else if (result == Result.WaitTimeout)
            {
                description = Result.WaitTimeout.Description;
            }
            else if (result == Result.Pending)
            {
                description = Result.Pending.Description;
            }
            else
            {
                description = "unknown reason";
            }

            var resultCode = result.ToString();
            throw (new ApplicationException($"Graphics card suspended ({resultCode}: {description}): {e.Message}"));
        }
    }

    public static void RebuildUiCopyTextureIfRequired()
    {
        var needsRebuild = _uiCopyTexture == null ||
                           _uiCopyTexture.Description.Width != Main.SwapChain.Description.ModeDescription.Width ||
                           _uiCopyTexture.Description.Height != Main.SwapChain.Description.ModeDescription.Height;

        if (!needsRebuild)
            return;

        // Create a shader resource-compatible texture
        var textureDesc = new Texture2DDescription
        {
            Width = Main.SwapChain.Description.ModeDescription.Width,
            Height = Main.SwapChain.Description.ModeDescription.Height,
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

    private static Texture2D _uiCopyTexture;
    public static ShaderResourceView UiCopyTextureSrv { get; private set; }
}
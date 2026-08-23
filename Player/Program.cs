// NOTE: Enabling this will require Windows Graphics Tools feature to be enabled
// This will prevent the player from running on most Windows systems.
//#define FORCE_D3D_DEBUG
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using ManagedBass;
using Newtonsoft.Json;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Compilation;
using T3.Core.DataTypes.Vector;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Settings;
using T3.Core.Resource;
using T3.Core.SystemUi;
using Device = SharpDX.Direct3D11.Device;
using Resource = SharpDX.Direct3D11.Resource;
using SharpDX.Windows;
using System.Windows.Forms;
using SilkWindows;
using T3.Core.Resource.ShaderCompiling;
using T3.Core.Utils;
using T3.Serialization;
using DeviceContext = SharpDX.Direct3D11.DeviceContext;
using Factory = SharpDX.DXGI.Factory;
using FillMode = SharpDX.Direct3D11.FillMode;
using ResourceManager = T3.Core.Resource.ResourceManager;
using VertexShader = T3.Core.DataTypes.VertexShader;
using PixelShader = T3.Core.DataTypes.PixelShader;
using ShaderCompiler = T3.Core.Resource.ShaderCompiling.ShaderCompiler;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Player;

/// <summary>
/// Bootstraps the standalone player, loads exported content, and starts the render loop.
/// </summary>
internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Must run before any code that may trigger assembly resolution.
        T3.Core.Diagnostics.AssemblyLoadDiagnostics.Install();

        CoreUi.Instance = new MsForms.MsForms();
        var silkWindows = new SilkWindowProvider();
        BlockingWindow.Instance = silkWindows;
        TrySetDialogFonts(silkWindows);

        var settingsPath = Path.Combine(FileLocations.StartFolder, ExportSettings.FileName);
        if (!JsonUtils.TryLoadingJson(settingsPath, out ExportSettings exportSettings) || exportSettings.Export == null)
        {
            var message = $"Failed to load export settings from \"{settingsPath}\". Exiting!";
            Log.Error(message);
            BlockingWindow.Instance.ShowMessageBox(message);
            return;
        }

        CoreSettings.Config = exportSettings.ConfigData;

        var playerDataDirectory = ResolvePlayerDataDirectory(exportSettings);
        var fileWriter = FileWriter.CreateDefault(playerDataDirectory, out var logPath);
        try
        {
            Log.AddWriter(fileWriter);

            if (!PlayerStartupOptions.TryParseCommandLine(args, exportSettings.ApplicationTitle, exportSettings.Author, out var commandLine, out var helpText))
            {
                BlockingWindow.Instance.ShowMessageBox(helpText, exportSettings.ApplicationTitle);
                return;
            }

            var lastUsedPath = Path.Combine(playerDataDirectory, "playerSettings.json");
            _startupOptions = PlayerStartupOptions.Resolve(exportSettings, commandLine, lastUsedPath);
            var displays = silkWindows.GetDisplays();

            var showDialog = commandLine.ForceDialog || (!commandLine.NoDialog && !exportSettings.Export.SkipStartupDialog);
            if (showDialog)
            {
                var dialog = new PlayerStartupDialog(exportSettings.ApplicationTitle, exportSettings.Author, displays, _startupOptions);
                var primary = _startupOptions.ResolveDisplay(displays);
                var dialogSize = new Vector2(520, 330);
                var dialogOptions = new SimpleWindowOptions(dialogSize, 60, true, false, true,
                                                            new Vector2(primary.Bounds.X + (primary.Bounds.Width - dialogSize.X) / 2,
                                                                        primary.Bounds.Y + (primary.Bounds.Height - dialogSize.Y) / 2));
                var result = silkWindows.Show(exportSettings.ApplicationTitle, dialog, dialogOptions);
                if (result == null)
                {
                    Log.Info("Startup cancelled.");
                    return;
                }

                _startupOptions = result;
                _startupOptions.SaveAsLastUsed(lastUsedPath);
            }

            if (_startupOptions.ShowLogs)
            {
                ConsoleWindow.Show();
                Log.AddWriter(new ConsoleWriter());
            }

            var display = _startupOptions.ResolveDisplay(displays);

            Log.Info($"Starting {exportSettings.ApplicationTitle} with id {exportSettings.OperatorId} by {exportSettings.Author}.");
            Log.Info($"Build: {exportSettings.BuildId}, Editor: {exportSettings.EditorVersion}");
            Log.Info($"Startup options: {_startupOptions} on {display}");

            // No BuildId in the path: the cache is keyed by shader content, so it stays valid across re-exports.
            ShaderCompiler.ShaderCacheSubdirectory = Path.Combine("Player",
                                                                  exportSettings.Author,
                                                                  exportSettings.ApplicationTitle,
                                                                  exportSettings.OperatorId.ToString());

            var resolution = new Int2(_startupOptions.Width, _startupOptions.Height);
            _vsyncInterval = Convert.ToInt16(_startupOptions.VSync);

            var iconPath = Path.Combine(SharedResources.EditorResourcesDirectory, "images", "t3.ico");
            var gotIcon = File.Exists(iconPath);

            Icon icon;
            if (!gotIcon)
            {
                Log.Warning("Failed to load icon from " + iconPath);
                icon = null;
            }
            else
            {
                icon = new Icon(iconPath);
            }

            _renderForm = new RenderForm(exportSettings.ApplicationTitle)
                              {
                                  ClientSize = new Size(resolution.X, resolution.Y),
                                  AllowUserResizing = false,
                                  Icon = icon,
                                  StartPosition = FormStartPosition.Manual,
                              };

            // Center on the chosen display; borderless fullscreen then covers that display.
            var displayBounds = display.Bounds;
            _renderForm.Location = new Point(displayBounds.X + Math.Max(0, (displayBounds.Width - _renderForm.Width) / 2),
                                             displayBounds.Y + Math.Max(0, (displayBounds.Height - _renderForm.Height) / 2));

            var windowHandle = _renderForm.Handle;

            // "Fullscreen" is a borderless window covering the screen. DXGI exclusive fullscreen is
            // avoided on purpose: it silently drops to windowed on focus loss (Alt+Tab), requires
            // ResizeBuffers after every mode change and minimizes the window, which led to
            // DXGI_ERROR_INVALID_CALL crashes. Flip-model swap chains get direct scan-out anyway.
            if (_startupOptions.Fullscreen)
            {
                SetBorderlessFullScreen(true);
            }

            // SwapChain description
            var desc = new SwapChainDescription
                           {
                               BufferCount = 3,
                               ModeDescription = new ModeDescription(_renderForm.ClientSize.Width, _renderForm.ClientSize.Height,
                                                                     new Rational(60, 1), Format.R8G8B8A8_UNorm),
                               IsWindowed = true,
                               OutputHandle = windowHandle,
                               SampleDescription = new SampleDescription(1, 0),
                               SwapEffect = SwapEffect.FlipDiscard,
                               Flags = SwapChainFlags.AllowModeSwitch,
                               Usage = Usage.RenderTargetOutput,
                           };

            //Try to load 11.1 if possible, revert to 11.0 auto
            FeatureLevel[] levels =
{
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
            };

            // Create Device and SwapChain
#if DEBUG || FORCE_D3D_DEBUG
            var deviceCreationFlags = DeviceCreationFlags.Debug | DeviceCreationFlags.BgraSupport;
#else
                var deviceCreationFlags = DeviceCreationFlags.None;
#endif
            Device.CreateWithSwapChain(DriverType.Hardware, deviceCreationFlags, desc, out _device, out _swapChain);
            ResourceManager.Init(_device);
            _deviceContext = _device.ImmediateContext;

            CoreUi.Instance.Cursor.SetVisible(!_isFullScreen);
            _backBufferSize = _renderForm.ClientSize;

            // Ign ore all windows events
            var factory = _swapChain.GetParent<Factory>();
            factory.MakeWindowAssociation(_renderForm.Handle, WindowAssociationFlags.IgnoreAll);

            InitializeInput(_renderForm);

            // New RenderTargetView from the backbuffer
            _backBuffer = Resource.FromSwapChain<SharpDX.Direct3D11.Texture2D>(_swapChain, 0);
            _renderView = new RenderTargetView(_device, _backBuffer);

            var shaderCompiler = new DX11ShaderCompiler
                                     {
                                         Device = _device
                                     };
            ShaderCompiler.Instance = shaderCompiler;
                
            SharedResources.Initialize();
                
            _fullScreenPixelShaderResource = SharedResources.FullScreenPixelShaderResource;
            _fullScreenVertexShaderResource = SharedResources.FullScreenVertexShaderResource;

            LoadOperators();

            if(!SymbolRegistry.TryGetSymbol(exportSettings.OperatorId, out var demoSymbol))
            {
                CloseApplication(true, $"Failed to find [{exportSettings.ApplicationTitle}] with id {exportSettings.OperatorId}");
                return;
            }

            Log.Debug($"Try to load playback settings for {demoSymbol}");
            var playbackSettings = demoSymbol.CompositionSettings;
            if (playbackSettings != null)
            {
                Log.Debug("Playback settings: " + JsonConvert.SerializeObject(
                                                                              playbackSettings,
                                                                              Formatting.Indented
                                                                             ));
            }
            else
            {
                Log.Warning($"No playback settings defined");

            }
            
            _playback = new Playback
                            {
                                Settings = playbackSettings
                            };

            // Create instance of project op, all children are create automatically

            if (!demoSymbol.TryGetParentlessInstance(out _project))
            {
                CloseApplication(true, $"Failed to create instance of project op {demoSymbol}");
                return;
            }
                
            _evalContext = new EvaluationContext();

            var prerenderRequired = false;

            _resolution = resolution;

            // Init wasapi input if required
            if (playbackSettings is { Playback.AudioSource: CompositionSettings.AudioSources.ProjectSoundTrack })
            {
                // Cache handles for every clip with a valid AssetPath so the per-frame
                // render loop can register them all. The first IsMainSoundtrack=true clip
                // is also stored in _soundtrackHandle for end-of-timeline / preload semantics.
                foreach (var clip in playbackSettings.Playback.AudioClips)
                {
                    if (string.IsNullOrEmpty(clip.AssetPath))
                        continue;
                    var handle = new AudioClipResourceHandle(clip, _project);
                    _allSoundtrackHandles.Add(handle);
                    if (clip.IsMainSoundtrack && _soundtrackHandle == null)
                        _soundtrackHandle = handle;
                }

                // Migrated projects carry the soundtrack as an [AudioClip] op instead of a settings-list
                // entry — the union in TryGetMainSoundtrack finds an op-flagged clip among the project's
                // children. Needed for stream preload and the end-of-timeline check; per-frame playback
                // registration comes from AudioClipCollector in the render loop.
                if (_soundtrackHandle == null && playbackSettings.TryGetMainSoundtrack(_project, out var opSoundtrack))
                {
                    _soundtrackHandle = opSoundtrack;
                    _allSoundtrackHandles.Add(opSoundtrack);
                }

                if (_soundtrackHandle != null)
                {
                    if (_soundtrackHandle.TryGetFileResource(out var file))
                    {
                        // BPM lives on Playback now; the settings loader migrated any legacy
                        // per-clip BPM into playbackSettings.Playback.Bpm.
                        _playback.Bpm = playbackSettings.Playback.Bpm;
                        // Pre-register every clip so all streams load before the first frame.
                        foreach (var h in _allSoundtrackHandles)
                            AudioEngine.UseSoundtrackClip(h, 0);
                        AudioEngine.CompleteFrame(_playback, Playback.LastFrameDuration); // Initialize
                        prerenderRequired = true;
                    }
                    else
                    {
                        Log.Warning($"Can't find soundtrack {_soundtrackHandle.Clip.AssetPath}");
                        _soundtrackHandle = null;
                    }
                }
            }

            var rasterizerDesc = new RasterizerStateDescription
                                     {
                                         FillMode = FillMode.Solid,
                                         CullMode = CullMode.None,
                                         IsScissorEnabled = false,
                                         IsDepthClipEnabled = false
                                     };
            _rasterizerState = new RasterizerState(_device, rasterizerDesc);

            foreach (var output in _project.Outputs)
            {
                if (output is Slot<Texture2D> textureSlot)
                {
                    if (_textureOutput == null)
                        _textureOutput = textureSlot;
                    else
                    {
                        var message = "Multiple texture outputs found. Only the first one will be used.";
                        Log.Warning(message);
                        break;
                    }
                }
            }

            if (_textureOutput == null)
            {
                var sb = new StringBuilder();
                var slots = _project.Outputs.Where(x => x is not null).ToArray();
                sb.AppendLine("Found the following outputs:");
                foreach (var slot in slots)
                {
                    sb.AppendLine($"{slot.GetType()} | {slot.ValueType} ({slot.ValueType.Assembly.ToString()}\n");
                }

                sb.AppendLine();
                sb.AppendLine("Expected:");
                sb.Append($"{typeof(Slot<Texture2D>).FullName} | {typeof(Texture2D).FullName} ({typeof(Texture2D).Assembly.ToString()}\n");
                var message = $"Failed to find texture output. \n{sb}";
                CloseApplication(true, message);
                return;
            }

            // TODO - implement proper shader pre-compilation as an option to instance instantiation
            // move this to core?
            // Sample some frames to preload all shaders and resources
            if (prerenderRequired)
            {
                PreloadShadersAndResources(_soundtrackHandle.Clip.LengthInSeconds, _resolution, _playback, _deviceContext, _evalContext, _textureOutput, _swapChain,
                                           _renderView);
            }

            // Start playback           
            _playback.Update();
            _playback.TimeInBars = 0;
            _playback.PlaybackSpeed = 1.0;

            try
            {
                // Main loop
                RenderLoop.Run(_renderForm, RenderCallback);
            }
            catch (TimelineEndedException)
            {
                Log.Info($"Program ended at the end of the timeline: {_playback.TimeInSecs:0.00}s / {_playback.TimeInBars:0.00} bars");
                CloseApplication(false, null);
            }
            catch (Exception e)
            {
                var errorMessage = "Exception in main loop:\n" + e;
                CloseApplication(true, errorMessage);
                Log.Error(errorMessage);
                fileWriter.Dispose(); // flush and close
                BlockingWindow.Instance.ShowMessageBox(errorMessage);
            }

        }
        catch (Exception e)
        {
            CloseApplication(true, "Exception in initialization:\n" + e);
        }
            
        return;

        void CloseApplication(bool error, string message)
        {
            CoreUi.Instance.Cursor.SetVisible(true);
            ShaderCompiler.Shutdown();
            bool openLogs = false;
                
            if (!string.IsNullOrWhiteSpace(message))
            {
                if (error)
                    Log.Error(message);
                else
                    Log.Info(message);

                const int maxLines = 10;
                message = StringUtils.TrimStringToLineCount(message, maxLines).ToString();

                if (error)
                {
                    message += "\n\nDo you want to open the log file?";

                    var result = BlockingWindow.Instance.ShowMessageBox(message, $"{exportSettings.ApplicationTitle} crashed /:", "Yes", "No");
                    openLogs = result == "Yes";
                }
            }
                    
            fileWriter.Dispose(); // flush and close

            // Release all resources
            try
            {
                _renderView?.Dispose();
                _backBuffer?.Dispose();
                _deviceContext?.ClearState();
                _deviceContext?.Flush();
                _device?.Dispose();
                _deviceContext?.Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"Failed to dispose of resources: {e}");
            }

            if (openLogs)
            {
                CoreUi.Instance.OpenWithDefaultApplication(logPath);
            }
                
            CoreUi.Instance.ExitApplication();
        }
    }

    /// <summary>
    /// Toggles between the normal window and a borderless window covering the screen the window is on.
    /// The swap chain follows the new client size on the next frame (see <see cref="EnsureBackBufferSize"/>).
    /// </summary>
    private static void SetBorderlessFullScreen(bool enable)
    {
        if (enable == _isFullScreen)
            return;

        _isFullScreen = enable;
        if (enable)
        {
            _windowedBounds = _renderForm.Bounds;
            _windowedBorderStyle = _renderForm.FormBorderStyle;
            _renderForm.WindowState = FormWindowState.Normal;
            _renderForm.FormBorderStyle = FormBorderStyle.None;
            _renderForm.Bounds = Screen.FromControl(_renderForm).Bounds;
        }
        else
        {
            _renderForm.FormBorderStyle = _windowedBorderStyle;
            _renderForm.Bounds = _windowedBounds;
        }

        CoreUi.Instance.Cursor.SetVisible(!enable);
    }

    /// <summary>
    /// Resizes the swap chain when the window's client size changed (fullscreen toggle, DPI change).
    /// Called once per frame before rendering.
    /// </summary>
    private static void EnsureBackBufferSize()
    {
        var clientSize = _renderForm.ClientSize;
        if (clientSize == _backBufferSize || clientSize.Width == 0 || clientSize.Height == 0)
            return;

        RebuildBackBuffer(_renderForm, _device, ref _renderView, ref _backBuffer, _swapChain);
    }

    private static void RebuildBackBuffer(RenderForm form, Device device, ref RenderTargetView rtv, ref SharpDX.Direct3D11.Texture2D buffer, SwapChain swapChain)
    {
        // ResizeBuffers requires that no reference to the back buffer survives - including a
        // binding on the output merger. A still-bound RTV leaves the pipeline in undefined
        // state which can escalate to DXGI_ERROR_DEVICE_HUNG on the next Present.
        device.ImmediateContext.OutputMerger.SetTargets((RenderTargetView)null);
        rtv.Dispose();
        buffer.Dispose();

        // Preserve the swap chain's existing flags across the resize.
        swapChain.ResizeBuffers(3, form.ClientSize.Width, form.ClientSize.Height, Format.Unknown, swapChain.Description.Flags);
        buffer = Resource.FromSwapChain<SharpDX.Direct3D11.Texture2D>(swapChain, 0);
        rtv = new RenderTargetView(device, buffer);
        _backBufferSize = form.ClientSize;
    }

    /// <summary>
    /// Logs and remembered settings live in a .temp folder next to the executable, where users look for them.
    /// Falls back to the roaming app-data folder when the export location is read-only.
    /// </summary>
    private static string ResolvePlayerDataDirectory(ExportSettings exportSettings)
    {
        var localDirectory = Path.Combine(FileLocations.StartFolder, ".temp");
        try
        {
            Directory.CreateDirectory(localDirectory);
            var probePath = Path.Combine(localDirectory, ".write-test");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return localDirectory;
        }
        catch (Exception)
        {
            return Path.Combine(FileLocations.SettingsDirectory, "Player", exportSettings.Author, exportSettings.ApplicationTitle);
        }
    }

    /// <summary>
    /// Uses the editor's UI fonts for the startup dialog and message boxes when the export ships them.
    /// </summary>
    private static void TrySetDialogFonts(SilkWindowProvider silkWindows)
    {
        var fontDirectory = Path.Combine(SharedResources.EditorResourcesDirectory, "fonts");
        var regularPath = Path.Combine(fontDirectory, "Inter-Regular.ttf");
        var boldPath = Path.Combine(fontDirectory, "Inter-SemiBold.ttf");
        var lightPath = Path.Combine(fontDirectory, "Inter-Light.ttf");
        if (!File.Exists(regularPath) || !File.Exists(boldPath) || !File.Exists(lightPath))
            return;

        silkWindows.SetFonts(new FontPack(new TtfFont(regularPath, 18),
                                          new TtfFont(boldPath, 18),
                                          new TtfFont(regularPath, 14),
                                          new TtfFont(lightPath, 30)));
    }

    /// <summary>
    /// The player is a windowed application; a console is only attached when log output was requested.
    /// </summary>
    private static class ConsoleWindow
    {
        public static void Show()
        {
            if (!OperatingSystem.IsWindows())
                return;

            AllocConsole();
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();
    }

    private readonly struct PackageLoadInfo(
        PlayerSymbolPackage package,
        List<SymbolJson.SymbolReadResult> newlyLoadedSymbols)
    {
        public readonly PlayerSymbolPackage Package = package;
        public readonly List<SymbolJson.SymbolReadResult> NewlyLoadedSymbols = newlyLoadedSymbols;
    }

    // Private static bool _inResize;
    private static int _vsyncInterval;
    private static SwapChain _swapChain;
    private static RenderTargetView _renderView;
    private static SharpDX.Direct3D11.Texture2D _backBuffer;
    private static Instance _project;
    private static EvaluationContext _evalContext;
    private static Playback _playback;
    private static AudioClipResourceHandle _soundtrackHandle;
    // All clips registered with the engine each frame so multiple clips play simultaneously.
    // _soundtrackHandle above remains the first IsMainSoundtrack=true entry for end-of-timeline
    // and preload semantics.
    private static readonly List<AudioClipResourceHandle> _allSoundtrackHandles = new();
    private static DeviceContext _deviceContext;
    private static PlayerStartupOptions _startupOptions;
    private static RenderForm _renderForm;
    private static Texture2D _outputTexture;
    private static ShaderResourceView _outputTextureSrv;
    private static bool _loggedNullOutput;
    private static bool _isFullScreen;
    private static Size _backBufferSize;
    private static Rectangle _windowedBounds;
    private static FormBorderStyle _windowedBorderStyle;
    private static RasterizerState _rasterizerState;
    private static Resource<VertexShader> _fullScreenVertexShaderResource;
    private static Resource<PixelShader> _fullScreenPixelShaderResource;
    private static Device _device;
    private static Int2 _resolution;
    private static Slot<Texture2D> _textureOutput;
}
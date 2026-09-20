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
using T3.Graphics.Compat;
using T3.Graphics;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Compilation;
using T3.Core.DataTypes.Vector;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Core.Operator.Slots;
using T3.Core.Settings;
using T3.Core.Resource;
using T3.Core.SystemUi;
using Device = T3.Graphics.Compat.Device;
using Resource = T3.Graphics.Compat.Resource;
using SDL;
using SilkWindows;
using T3.Core.Resource.ShaderCompiling;
using T3.Core.Utils;
using T3.SdlPlatform;
using T3.Serialization;
using DeviceContext = T3.Graphics.Compat.DeviceContext;
using Factory = SharpDX.DXGI.Factory;
using FillMode = T3.Graphics.Compat.FillMode;
using ResourceManager = T3.Core.Resource.ResourceManager;
using VertexShader = T3.Core.DataTypes.VertexShader;
using PixelShader = T3.Core.DataTypes.PixelShader;
using ShaderCompiler = T3.Core.Resource.ShaderCompiling.ShaderCompiler;
using Texture2D = T3.Core.DataTypes.Texture2D;
using static SDL.SDL3;

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

        CoreUi.Instance = _coreUi;
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

        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
        {
            BlockingWindow.Instance.ShowMessageBox($"Failed to initialize SDL: {SDL_GetError()}");
            return;
        }

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
            var displays = _displayProvider.GetDisplays();

            // An installation comes up on its own displays with no one at the keyboard, so it never asks —
            // though --dialog still forces the question when someone is there to answer it.
            var skipDialog = exportSettings.Export.SkipStartupDialog
                             || exportSettings.Export.PlayerMode == CompositionSettings.PlayerModes.Installation;
            var showDialog = commandLine.ForceDialog || (!commandLine.NoDialog && !skipDialog);
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

            // Writable cache next to the executable; precompiled entries shipped with the export are read first.
            ShaderCompiler.ShaderCacheRootPath = playerDataDirectory;
            ShaderCompiler.ShaderCacheSubdirectory = FileLocations.ShaderCacheSubFolder;
            ShaderCompiler.ShaderCacheSeedDirectory = Path.Combine(FileLocations.StartFolder, FileLocations.ShaderCacheSubFolder);
            ShaderCompiler.PruneCache(TimeSpan.FromDays(30));

            var resolution = new Int2(_startupOptions.Width, _startupOptions.Height);
            _vsyncInterval = Convert.ToInt16(_startupOptions.VSync);

            var iconPath = Path.Combine(SharedResources.EditorResourcesDirectory, "images", "t3.ico");
            if (!File.Exists(iconPath))
            {
                Log.Warning("Failed to load icon from " + iconPath);
                iconPath = null;
            }

            // Centered on the chosen display; borderless fullscreen then covers that display.
            _mainWindow = new PlayerWindow(exportSettings.ApplicationTitle,
                                           new Size(resolution.X, resolution.Y),
                                           _displayProvider.GetDisplayId(display.Index),
                                           iconPath);
            if (_startupOptions.Fullscreen)
            {
                SetBorderlessFullScreen(true);
            }

            if (!OperatingSystem.IsWindows())
            {
                CloseApplication(true, "This player renders with Direct3D 11, which is only available on Windows.");
                return;
            }

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
            // BgraSupport is required for the Direct2D loading screen
            var deviceCreationFlags = DeviceCreationFlags.BgraSupport;
#endif
            // Which backend renders is decided here, and nothing above this line knows the difference.
            _backend = T3.Graphics.D3D11.D3D11Backend.Create();
            _device = new Device(_backend);
            ResourceManager.Init(_device);
            _deviceContext = _device.ImmediateContext;
            _mainWindow.CreateSwapChain(_device);

            CoreUi.Instance.Cursor.SetVisible(!_isFullScreen);

            var shaderCompiler = new DX11ShaderCompiler
                                     {
                                         Device = _device
                                     };
            ShaderCompiler.Instance = shaderCompiler;
                
            SharedResources.Initialize();
                
            _fullScreenPixelShaderResource = SharedResources.FullScreenPixelShaderResource;
            _fullScreenVertexShaderResource = SharedResources.FullScreenVertexShaderResource;

            // Loading runs in steps on this thread; the loading screen is redrawn between them.
            var loadReport = new PlayerLoadReport();
            _lastLogLine = new LastLogLineWriter();
            Log.AddWriter(_lastLogLine);
            _loadingScreen = new LoadingScreen(exportSettings.ApplicationTitle);
            _isLoading = true;
            _mainWindow.Show();
            PumpLoadingScreen("Loading operators...", LoadProgressOperatorsStart);

            loadReport.BeginStage("Load operators");
            if (!LoadOperators(loadReport))
            {
                CloseApplication(false, "Loading cancelled.");
                return;
            }

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

            LoadOutputSetup(displays, resolution);
            InitializeOutputWindows(displays);

            // Create instance of project op, all children are create automatically
            loadReport.BeginStage("Create instances");
            if (!PumpLoadingScreen("Creating operators...", LoadProgressInstance))
            {
                CloseApplication(false, "Loading cancelled.");
                return;
            }

            if (!demoSymbol.TryGetParentlessInstance(out _project))
            {
                CloseApplication(true, $"Failed to create instance of project op {demoSymbol}");
                return;
            }

            loadReport.InstanceCount = CountInstances(_project);
            loadReport.BeginStage("Prepare audio");
                
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

            // The sends are what a show puts on screen. Creating them also registers them, which is how the
            // compositor finds the content the setup routes — nothing else would ever instantiate them.
            ContentSupplierSearch.CollectUnder(_project, _sends);
            if (_sends.Count == 0)
            {
                CloseApplication(true, "Nothing to show: this project contains no [SendToOutput].\n"
                                       + "Connect what it renders to a [SendToOutput] and export it again.");
                return;
            }

            // TODO - implement proper shader pre-compilation as an option to instance instantiation
            // move this to core?
            // Sample some frames to preload all shaders and resources
            loadReport.BeginStage("Warm up shaders");
            if (prerenderRequired)
            {
                if (!PreloadShadersAndResources(_soundtrackHandle.Clip.LengthInSeconds, _resolution, _playback, _deviceContext, _evalContext,
                                                _mainWindow.RenderTargetView))
                {
                    CloseApplication(false, "Loading cancelled.");
                    return;
                }
            }
            else if (!PumpLoadingScreen("Warming up shaders...", LoadProgressPreloadStart))
            {
                CloseApplication(false, "Loading cancelled.");
                return;
            }

            PumpLoadingScreen("Starting...", LoadProgressPreloadEnd);
            loadReport.ShadersCompiled = ShaderCompiler.CompiledShaderCount;
            loadReport.ShadersFromCache = ShaderCompiler.CachedShaderCount;
            loadReport.CountAssets(Path.Combine(FileLocations.StartFolder, FileLocations.OperatorsSubFolder), FileLocations.AssetsSubfolder);
            loadReport.Complete();
            loadReport.LogAndSave(Path.Combine(playerDataDirectory, "loadReport.json"));

            _isLoading = false;
            Log.RemoveWriter(_lastLogLine);
            _loadingScreen.Dispose();
            _loadingScreen = null;

            // Start playback           
            _playback.Update();
            _playback.TimeInBars = 0;
            _playback.PlaybackSpeed = 1.0;

            try
            {
                _coreUi.IsEventLoopRunning = true;
                while (PumpEvents() && !_coreUi.QuitRequested)
                {
                    RenderCallback();
                }

                CloseApplication(false, null);
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
            _loadingScreen?.Dispose();
            _loadingScreen = null;
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
                DisposeOutputWindows();
                OutputStreaming.DisposeAll();
                _mainWindow?.Dispose();
                _deviceContext?.ClearState();
                _deviceContext?.Flush();
                _backend?.Dispose();
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
    /// Toggles between the normal window and a borderless window covering the display the window is on.
    /// The swap chain follows the new size on the next frame (see <see cref="EnsureBackBufferSize"/>).
    /// </summary>
    private static void SetBorderlessFullScreen(bool enable)
    {
        if (enable == _isFullScreen)
            return;

        _isFullScreen = enable;
        _mainWindow.SetFullscreen(enable);
        CoreUi.Instance.Cursor.SetVisible(!enable);
    }

    /// <summary>Called once per frame before rendering.</summary>
    private static void EnsureBackBufferSize()
    {
        _mainWindow.EnsureBackBufferSize(_device, _releaseLoadingScreenTarget);
    }

    /** The loading screen's Direct2D target references the back buffer, which blocks a swap chain resize. */
    private static void ReleaseLoadingScreenTarget()
    {
        _loadingScreen?.ReleaseBackBufferResources();
    }

    /// <summary>
    /// Publishes the venue shipped beside the player, so operators that read the active setup (the stage
    /// geometry, the projector camera) work in an export as they do in the editor. An output left at 0 × 0
    /// takes the size of what shows it: its display when bound, else the resolution this player runs at.
    /// </summary>
    private static void LoadOutputSetup(IReadOnlyList<T3.SystemUi.DisplayInfo> displays, Int2 windowResolution)
    {
        var metaFolder = Path.Combine(FileLocations.StartFolder, Setup.FolderName);
        if (!SetupFiles.TryLoad(metaFolder, out var setup, out var machineConfig, out _) || setup == null)
        {
            Log.Debug("No output setup shipped with this project.");
            return;
        }

        // Called once at startup, so the capturing lambda costs nothing that matters.
        SetupFiles.ResolveCanvasResolutions(setup, machineConfig,
                                            binding => binding switch
                                                           {
                                                               null => windowResolution,
                                                               { IsStream: true } => SetupFiles.UnboundResolution,
                                                               _ when binding.DisplayIndex >= 0 && binding.DisplayIndex < displays.Count
                                                                   => new Int2(displays[binding.DisplayIndex].CurrentMode.Width,
                                                                               displays[binding.DisplayIndex].CurrentMode.Height),
                                                               _ => windowResolution,
                                                           });
        ActiveSetup.Current = setup;
        ActiveSetup.Machine = machineConfig;
        Log.Info($"Loaded output setup \"{setup.Name}\": {setup.Outputs.Count} output(s), {setup.Surfaces.Count} surface(s).");
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
    private static readonly SdlCoreUi _coreUi = new();
    private static readonly SdlDisplayProvider _displayProvider = new();
    private static readonly Action _releaseLoadingScreenTarget = ReleaseLoadingScreenTarget;
    private static PlayerWindow _mainWindow;
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
    private static Texture2D _outputTexture;
    private static ShaderResourceView _outputTextureSrv;
    private static bool _loggedNullOutput;
    private static bool _isFullScreen;
    private static RasterizerState _rasterizerState;
    private static Resource<VertexShader> _fullScreenVertexShaderResource;
    private static Resource<PixelShader> _fullScreenPixelShaderResource;
    private static T3.Graphics.D3D11.D3D11Backend? _backend;
    private static Device _device;
    private static Int2 _resolution;
    private static readonly List<Instance> _sends = [];
}
#nullable enable
using SharpDX.Direct3D11;
using SilkWindows;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using T3.Core.Compilation;
using T3.Core.IO;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Core.UserData;
using T3.Editor.App;
using T3.Editor.Compilation;
using T3.Editor.Gui;
using T3.Editor.Gui.Interaction.Camera;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.StartupCheck;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.SystemUi;
using T3.Editor.UiModel.Helpers;
using T3.MsForms;
using T3.SystemUi;

namespace T3.Editor;

internal static class Program
{
    public static IUiContentDrawer? UiContentContentDrawer;
    public static Device? Device { get; private set; }

    public static Version Version => RuntimeAssemblies.Version;
    private static string? _versionText;
    
    public static string VersionText => _versionText ??= Version.ToBasicVersionString();


    private static string? _readableVersion;
    public static string FormattedEditorVersion
    {
        get
        {
            if (_readableVersion != null) 
                return _readableVersion;
            
            var asm = typeof(Editor.Program).Assembly;
            var semver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                           ?.InformationalVersion; // "1.9.0-rc.1" (maybe "+sha")

            _readableVersion = semver ?? asm.GetName().Version?.ToString() ?? "0.0.0.0";
            
            var plusIndex = _readableVersion.IndexOf('+');
            var shortenedSha = string.Empty;
            if (plusIndex > 0)
            {
                var end = Math.Min(_readableVersion.Length, plusIndex + 1 + 6); // '+' + 6 hex
                shortenedSha = _readableVersion[(plusIndex+1)..end];
                _readableVersion = _readableVersion[..(plusIndex)];
            }
            
            #if DEBUG
            const string buildTypeSuffix = " Debug";
            #else
            const string buildTypeSuffix = "";
            #endif
            
            _readableVersion = $"v{_readableVersion} {shortenedSha}{buildTypeSuffix}";
            return _readableVersion;
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        // Not calling this first will cause exceptions...
        Console.WriteLine("Starting T3 Editor");
        Console.WriteLine("Creating EditorUi");
        EditorUi.Instance = new MsFormsEditor();
            
        var windowProvider = new SilkWindowProvider();
        var imguiContextLock = windowProvider.ContextLock;
        ImGuiWindowService.Instance = windowProvider;
        BlockingWindow.Instance = windowProvider;

        Console.WriteLine("Creating DX11ShaderCompiler");
        ShaderCompiler.Instance = new DX11ShaderCompiler();

        // Console.WriteLine("Validating startup location");
        // StartupValidation.ValidateNotRunningFromSystemFolder();
        
        // Console.WriteLine("Validating execution policy");
        // StartupValidation.ValidateExecutionPolicy();

        Console.WriteLine("Enabling DPI aware scaling");
        EditorUi.Instance.EnableDpiAwareScaling();

        var startupStopWatch = new Stopwatch();
        startupStopWatch.Start();

        #if !DEBUG
        CrashReporting.InitializeCrashReporting();
        #endif

        Console.WriteLine("Creating SplashScreen");
        ISplashScreen splashScreen = new SplashScreen.SplashScreen();

        var path = Path.Combine(SharedResources.Directory, "images", "editor", "t3-SplashScreen.png");
        splashScreen.Show(path);

        Console.WriteLine("Initializing logging");
        Log.AddWriter(splashScreen);
        Log.AddWriter(new ConsoleWriter());
        Log.AddWriter(FileWriter.CreateDefault(FileLocations.SettingsDirectory, out var logPath));
        Log.AddWriter(StatusErrorLine);
        Log.AddWriter(ConsoleLogWindow);
            
        Log.Info($"Starting {FormattedEditorVersion}");
            
        CrashReporting.LogPath = logPath;
        //if (IsStandAlone)
        {
            //StartupValidation.ValidateCurrentStandAloneExecutable();
        }
        //else
        {
            //StartupValidation.CheckInstallation();
        }

        StartUp.FlagBeginStartupSequence();

        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        ShaderCompiler.ShaderCacheSubdirectory = $"Editor_{VersionText}";
            
        // ReSharper disable once UnusedVariable
        var userSettings = new UserSettings(saveOnQuit: true);
        
        // ReSharper disable once UnusedVariable
        var projectSettings = new ProjectSettings(saveOnQuit: true);

        Log.Debug("Initializing ProgramWindows...");
        ProgramWindows.InitializeMainWindow(FormattedEditorVersion, out var device);

        Device = device;

        if (ShaderCompiler.Instance is not DX11ShaderCompiler shaderCompiler)
            throw new Exception("ShaderCompiler is not DX11ShaderCompiler");

        shaderCompiler.Device = device;

        Log.Debug("Initializing UiContentContentDrawer...");
        var contentDrawer = new WindowsUiContentDrawer();
        UiContentContentDrawer = contentDrawer;
        contentDrawer.Initialize(device, ProgramWindows.Main.Width, ProgramWindows.Main.Height, imguiContextLock, out var context);

        Log.Debug("Initialize Camera Interaction...");
        var spaceMouse = new SpaceMouse(ProgramWindows.Main.HwndHandle);
        CameraInteraction.ManipulationDevices = [spaceMouse];
        ProgramWindows.SetInteractionDevices(spaceMouse);

        Log.Debug("Initialize Resource Manager...");
        ResourceManager.Init(device);
        SharedResources.Initialize();

        Log.Debug("Initialize User Interface...");
        KeyActionHandling.InitializeFrame();
        KeyMapSwitching.Initialize();

        bool forceRecompileProjects;
            
        #if DEBUG
            forceRecompileProjects = false;
        #else
        forceRecompileProjects = args is {Length: > 0} && args.Any(arg => arg == "--force-recompile");
        #endif

        Log.Debug("Loading projects...");
        // Initialize UI and load complete symbol model
        if (!ProjectSetup.TryLoadAll(forceRecompileProjects, out var uiException))
        {
            Log.Error(uiException.Message + "\n\n" + uiException.StackTrace);
            var innerException = uiException.InnerException?.Message.Replace("\\r", "\r") ?? string.Empty;
            BlockingWindow.Instance.ShowMessageBox($"Loading Operators failed:\n\n{uiException.Message}\n{innerException}\n\n" +
                                                   $"This is liked caused by a corrupted operator file." +
                                                   $"\nPlease try restarting and restore backup.\n\n" + uiException,
                                                   @"Error", "Ok");
            EditorUi.Instance.ExitApplication();
        }

        SymbolAnalysis.UpdateSymbolUsageCounts();
            
        UiContentContentDrawer.InitializeScaling();
        UiContentUpdate.CheckScaling();
            
        // Setup file watching the operator source
        T3Ui.InitializeEnvironment();
            
        Log.RemoveWriter(splashScreen);
            
        if(UserSettings.Config.KeepTraceForLogMessages)
            Log.AddWriter(new Profiling.ProfilingLogWriterClass());
            
        splashScreen.Close();
        splashScreen.Dispose();

        // Initialize optional Viewer Windows
        ProgramWindows.InitializeSecondaryViewerWindow("TiXL Viewer", 640, 360);

        StartUp.FlagStartupSequenceComplete();

        startupStopWatch.Stop();
        Log.Info($"Startup took {startupStopWatch.ElapsedMilliseconds}ms.");

        UiContentUpdate.StartMeasureFrame();

        T3Style.Apply();
            
        // ReSharper disable once AccessToDisposedClosure
        ProgramWindows.Main.RunRenderLoop(UiContentContentDrawer.RenderCallback);
        IsShuttingDown = true;

        try
        {
            ProjectSetup.DisposePackages();
            UiContentContentDrawer.Dispose();
        }
        catch (Exception e)
        {
            BlockingWindow.Instance.ShowMessageBox("Exception during package shutdown: \n" + e);
        }


        // Release all resources
        try
        {
            ProgramWindows.Release();
        }
        catch (Exception e)
        {
            Log.Warning("Exception freeing resources: " + e.Message);
        }

        Log.Debug("Shutdown complete");
    }

    // Main loop
    public static readonly StatusErrorLine StatusErrorLine = new();
    public static readonly ConsoleLogWindow ConsoleLogWindow = new();
    public static string NewImGuiLayoutDefinition = string.Empty;
    public static bool IsShuttingDown;
}
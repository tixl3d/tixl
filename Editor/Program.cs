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
using T3.Core.Resource.ShaderCompiling;
using T3.Core.SystemUi;
using T3.Core.Settings;
using T3.Editor.App;
using T3.Editor.Compilation;
using T3.Editor.Gui;
using T3.Editor.Gui.Interaction.Camera;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.StartupCheck;
using T3.Editor.Migrations.AssetPaths;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.AssetLib;
using T3.Editor.Skills.Training;
using T3.Editor.SystemUi;
using T3.Editor.UiContentDrawing;
using T3.Editor.UiModel.Helpers;
using T3.MsForms;
using T3.SystemUi;
using ShaderCompiler = T3.Core.Resource.ShaderCompiling.ShaderCompiler;

namespace T3.Editor;

/// <summary>
/// Bootstraps the TiXL editor process, rendering context, and UI services.
/// </summary>
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
        // Must run before any code that may trigger assembly resolution.
        T3.Core.Diagnostics.AssemblyLoadDiagnostics.Install();

        // Must run before anything reads FileLocations.SettingsDirectory (e.g. the log path below).
        ApplyVersionIdOverrideArg(args);

        // Must run before the settings files are read — a restarted instance races the old
        // instance's save-on-quit writes otherwise.
        WaitForPredecessorArg(args);

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

        ApplyWindowArgs(args);

        Console.WriteLine("Creating SplashScreen");
        ISplashScreen splashScreen = new SplashScreen.SplashScreen();

        if (!SkipSplash)
        {
            var path = Path.Combine(SharedResources.EditorResourcesDirectory, "images", "t3-SplashScreen.png");
            splashScreen.Show(path);
        }

        Console.WriteLine("Initializing logging");
        if (!SkipSplash)
            Log.AddWriter(splashScreen);
        Log.AddWriter(new ConsoleWriter());
        Log.AddWriter(FileWriter.CreateDefault(FileLocations.SettingsDirectory, out var logPath));
        Log.AddWriter(StatusErrorLine);
        Log.AddWriter(ConsoleLogWindow);
            
        Log.Info($"Starting {FormattedEditorVersion}");

        if (TryGetDebugServerPortArg(args, out var debugServerPort))
            App.DebugProtocol.DebugServer.Start(debugServerPort);

        if (FileLocations.VersionIdOverride != null)
            Log.Info($"Settings folder overridden via '{FileLocations.VersionIdOverrideEnvVar}': {FileLocations.SettingsDirectory}");

        CrashReporting.LogPath = logPath;
        //if (IsStandAlone)
        {
            //StartupValidation.ValidateCurrentStandAloneExecutable();
        }
        //else
        {
            //StartupValidation.CheckInstallation();
        }

        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        ShaderCompiler.ShaderCacheSubdirectory = $"Editor_{VersionText}";
        ShaderCompiler.PruneCache(TimeSpan.FromDays(30));

        // ReSharper disable once UnusedVariable
        var userSettings = new UserSettings(saveOnQuit: true);

        // Initialize debug logging configuration from user settings
        UserSettings.InitializeGatedLogging();

        // ReSharper disable once UnusedVariable
        var projectSettings = new CoreSettings(saveOnQuit: true);

        if (UserSettings.Config.ProjectDirectories.Count == 0)
        {
            UserSettings.Config.ProjectDirectories.Add(FileLocations.DefaultProjectFolder);
        }

        // Run after UserSettings is initialized — the crash-recovery dialog needs
        // ProjectDirectories to locate per-project backups under .temp/Backup/.
        StartUp.FlagBeginStartupSequence();

        Log.Debug("Initializing ProgramWindows...");
        ProgramWindows.InitializeMainWindow(FormattedEditorVersion, out var device);
        AssetHandling.InitAssetTypes();
        
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

        // ReSharper disable once JoinDeclarationAndInitializer
        bool forceRecompileProjects;
            
        #if DEBUG
            forceRecompileProjects = false;
        #else
        forceRecompileProjects = args is {Length: > 0} && args.Any(arg => arg == "--force-recompile");
        #endif

        Log.Info("Start loading...");
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
        ConformAssetPaths.ConformAllPaths();
            
        UiContentContentDrawer.InitializeScaling();
        UiContentUpdate.SetupResourcesAndFontsWithScaling();
            
        // Setup file watching the operator source
        T3Ui.InitializeEnvironment();
        SkillTraining.Initialize();
            
        if (!SkipSplash)
            Log.RemoveWriter(splashScreen);

        if(UserSettings.Config.KeepTraceForLogMessages)
            Log.AddWriter(new Profiling.ProfilingLogWriterClass());

        if (!SkipSplash)
        {
            splashScreen.Close();
            splashScreen.Dispose();
        }

        // Initialize optional Viewer Windows
        ProgramWindows.InitializeSecondaryViewerWindow("TiXL Viewer", 640, 360);

        StartUp.FlagStartupSequenceComplete();

        startupStopWatch.Stop();
        Log.Info($"Startup took {startupStopWatch.ElapsedMilliseconds/1000:0.0}s.");

        UiContentUpdate.StartMeasureFrame();

        T3Style.Apply();
            
        // ReSharper disable once AccessToDisposedClosure
        ProgramWindows.Main.RunRenderLoop(UiContentContentDrawer.RenderCallback);
        IsShuttingDown = true;
        App.DebugProtocol.DebugServer.Stop();

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

    /// <summary>Client-area size requested via <c>--window &lt;w&gt;x&lt;h&gt;</c>; null starts maximized as usual.</summary>
    internal static System.Drawing.Size? WindowSizeOverride { get; private set; }

    /// <summary>Set via <c>--no-splash</c> — skips showing the splash screen (e.g. for protocol-driven sessions).</summary>
    internal static bool SkipSplash { get; private set; }

    /// <summary>
    /// Honors <c>--window=&lt;w&gt;x&lt;h&gt;</c> (or <c>--window &lt;w&gt;x&lt;h&gt;</c>) for a predictable
    /// non-maximized window, and <c>--no-splash</c>.
    /// </summary>
    private static void ApplyWindowArgs(string[] args)
    {
        const string windowFlag = "--window";
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--no-splash", StringComparison.OrdinalIgnoreCase))
            {
                SkipSplash = true;
                continue;
            }

            string? value = null;
            if (arg.StartsWith(windowFlag + "=", StringComparison.OrdinalIgnoreCase))
                value = arg[(windowFlag.Length + 1)..];
            else if (arg.Equals(windowFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                value = args[i + 1];

            if (value == null)
                continue;

            var parts = value.Split('x', 'X');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var width) && width > 100
                && int.TryParse(parts[1], out var height) && height > 100)
            {
                WindowSizeOverride = new System.Drawing.Size(width, height);
            }
        }
    }

    /// <summary>
    /// Honors <c>--debug-server=&lt;port&gt;</c> (or <c>--debug-server &lt;port&gt;</c>): opt-in flag
    /// starting the local JSON-lines debug server on 127.0.0.1.
    /// </summary>
    private static bool TryGetDebugServerPortArg(string[] args, out int port)
    {
        const string flag = "--debug-server";
        port = 0;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? value = null;

            if (arg.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))
                value = arg[(flag.Length + 1)..];
            else if (arg.Equals(flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                value = args[i + 1];

            if (value != null && int.TryParse(value, out port) && port is > 0 and < 65536)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Honors <c>--override-version-id=&lt;id&gt;</c> (or <c>--override-version-id &lt;id&gt;</c>) by setting
    /// <see cref="FileLocations.VersionIdOverrideEnvVar"/> for this process, so two editor instances of the
    /// same build keep separate settings/project folders. Must run before any <see cref="FileLocations"/> access.
    /// </summary>
    private static void ApplyVersionIdOverrideArg(string[] args)
    {
        const string flag = "--override-version-id";
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? value = null;

            if (arg.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))
                value = arg[(flag.Length + 1)..];
            else if (arg.Equals(flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                value = args[i + 1];

            if (string.IsNullOrWhiteSpace(value))
                continue;

            Environment.SetEnvironmentVariable(FileLocations.VersionIdOverrideEnvVar, value);
            return;
        }
    }

    /// <summary>
    /// Honors <c>--wait-for-exit=&lt;pid&gt;</c> by blocking until that process has exited (max 15s).
    /// Passed by in-app restarts (e.g. after a backup restore): the new instance must not read the
    /// settings files while the old instance's save-on-quit handlers are still writing them.
    /// </summary>
    private static void WaitForPredecessorArg(string[] args)
    {
        const string flag = "--wait-for-exit=";
        foreach (var arg in args)
        {
            if (!arg.StartsWith(flag, StringComparison.Ordinal))
                continue;

            if (!int.TryParse(arg[flag.Length..], out var pid))
                return;

            try
            {
                using var predecessor = Process.GetProcessById(pid);
                Console.WriteLine($"Waiting for previous instance (pid {pid}) to exit...");
                if (!predecessor.WaitForExit(15_000))
                    Console.WriteLine("Previous instance did not exit in time. Continuing...");
            }
            catch (ArgumentException)
            {
                // Process already gone — nothing to wait for.
            }
            return;
        }
    }

    // Main loop
    public static readonly StatusErrorLine StatusErrorLine = new();
    public static readonly ConsoleLogWindow ConsoleLogWindow = new();
    public static string NewImGuiLayoutDefinition = string.Empty;
    public static bool IsShuttingDown;
}
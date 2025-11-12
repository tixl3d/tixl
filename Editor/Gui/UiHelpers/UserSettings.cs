#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.UserData;
using T3.Editor.Compilation;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.TimeLine;

// ReSharper disable MemberCanBeInternal

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Saves view layout, currently open node and other user settings 
/// </summary>
///  todo - make internal, make extendable by external packaages
public sealed class UserSettings : Settings<UserSettings.ConfigData>
{
    internal UserSettings(bool saveOnQuit) : base("userSettings.json", saveOnQuit: saveOnQuit)
    {
    }

    public sealed class ConfigData
    {
        /// <summary>
        /// Last saved user view for symbolChildIds.
        /// </summary>
        /// <remarks>
        /// Note that we are using symbolChildId instead of symbol, because we can assume that
        /// symbolChildIds provide more context, e.g. there are many LayerId instances with different
        /// Ids and the user scope might be different.
        /// </remarks>
        //public readonly Dictionary<Guid, CanvasScope> OperatorViewSettings = new();
        public readonly Dictionary<Guid, ImRect> ViewedCanvasAreaForSymbolChildId = new();

        public readonly Dictionary<string, Guid> LastOpsForWindows = new();

        [JsonConverter(typeof(StringEnumConverter))]
        public GraphHoverModes HoverMode = GraphHoverModes.LastValue;

        // Projects

        public string ProjectsFolder = FileLocations.DefaultProjectFolder;

        // UI-Elements
        public bool ShowThumbnails = true;
        public bool ShowMainMenu = true;
        public bool ShowTitleAndDescription = true;
        public bool ShowToolbar = true;
        public bool ShowTimeline = true;
        public bool ShowMiniMap = false;
        public bool ShowInteractionOverlay = false;
        public float LayerHeight = 18;

        // UI-State
        public float UiScaleFactor = 1;
        public bool FullScreen = false;
        public bool FocusMode = false;
        public int WindowLayoutIndex = 0;
        public bool EnableIdleMotion = true;
        public bool SuspendRenderingWhenHidden = true;

        /** Windows' implementation of mirroring a display is extremely slow and can
        * take up to 50% of the GPU time. Enabling this work around will copy the Main window
        * SwapChain buffer into a texture that is used for the 2nd viewer window.
        */
        public bool MirrorUiOnSecondView = false;

        public bool EnableKeyboardShortCuts = true;

        public GraphStyles GraphStyle = GraphStyles.Magnetic;

        // Interaction
        public bool WarnBeforeLibEdit = true;
        public bool SmartGroupDragging = false;

        public readonly bool ShowExplicitTextureFormatInOutputWindow = false;
        public bool UseArcConnections = true;
        public bool ResetTimeAfterPlayback;
        public float SnapStrength = 5;
        public ValueEditMethods ValueEditMethod;
        public int ValueEditSmoothing = 6;
        public float ScrollSmoothing = 0.06f;

        // Mag Graph
        public bool DisconnectOnUnsnap = true;
        public float MaxCurveRadius = 350;
        public int MaxSegmentCount = 32;
        public bool EnableHorizontalSnapping = true;

        public bool AddSpacesToParameterNames = true;

        public float ClickThreshold = 5; // Increase for high-res display and pen tablets
        public bool AdjustCameraSpeedWithMouseWheel = false;
        public float CameraSpeed = 1;

        public bool MiddleMouseButtonZooms = false;

        public FrameStepAmount FrameStepAmount = FrameStepAmount.FrameAt30Fps;

        public bool MouseWheelEditsNeedCtrlKey = true;
        public bool AutoPinAllAnimations = false;

        public readonly float KeyboardScrollAcceleration = 2.5f;

        public bool VariationLiveThumbnails = true;
        public bool VariationHoverPreview = true;

        public bool EditorHoverPreview = true;

        // Asset Lib
        public bool SyncWithOperatorSelection = true;
        public bool ScrollAssetLibToActive = true;
        
        // Load Save
        public string UserName = UndefinedUserName;
        public bool EnableAutoBackup = true;

        // Other settings
        public float GizmoSize = 100;

        // Fullscreen settings
        public int FullScreenIndexMain = 0;
        // Output window
        public int FullScreenIndexViewer = 0;
        public Vector4 OutputArea = new(0,0,0,0);

        // Timeline
        public float TimeRasterDensity = 1f;

        // Space mouse
        public float SpaceMouseRotationSpeedFactor = 1f;
        public float SpaceMouseMoveSpeedFactor = 1f;
        public float SpaceMouseDamping = 0.5f;

        // Rendering (controlled from render windows)
        public string RenderVideoFilePath = "./Render/render-v01.mp4";
        public string RenderSequenceFilePath = "./ImageSequence/";
        public string RenderSequenceFileName = "Output_";

        // Profiling and debugging
        public bool EnableFrameProfiling = true;
        public bool KeepTraceForLogMessages = false;
        public bool EnableGCProfiling = false;
        public bool ShowOperatorStats = false;

        public CompilerOptions.Verbosity CompileCsVerbosity = CompilerOptions.Verbosity.Normal;

        [JsonConverter(typeof(StringEnumConverter))]
        public TimeFormat.TimeDisplayModes TimeDisplayMode = TimeFormat.TimeDisplayModes.Bars;

        public readonly List<Bookmark> Bookmarks = [];

        public string ColorThemeName = string.Empty;

        // Other

        public string KeyBindingName = string.Empty;

        public bool ExpandSpectrumVisualizerVertically = true;
        public int GridOutputColumnCount = 16;

        //private string _defaultNewProjectDirectory = _defaultProjectFolder;
        //public string DefaultNewProjectDirectory => _defaultNewProjectDirectory ??= _defaultProjectFolder;

        private static readonly string _defaultProjectFolder = FileLocations.DefaultProjectFolder;
    }

    public enum ValueEditMethods
    {
        InfinitySlider,
        RadialSlider,
        JogDial,
        ValueLadder,
    }

    public enum GraphHoverModes
    {
        Disabled,
        Live,
        LastValue,
    }

    public enum GraphStyles
    {
        Legacy,
        Magnetic,
    }

    internal static bool IsUserNameDefined()
    {
        return !string.IsNullOrEmpty(Config.UserName) && Config.UserName != UndefinedUserName;
    }

    internal const string UndefinedUserName = "unknown";

    internal static Guid GetLastOpenOpForWindow(string windowTitle)
    {
        return Config.LastOpsForWindows.TryGetValue(windowTitle, out var id) ? id : Guid.Empty;
    }

    internal static void SaveLastViewedOpForWindow(string title, Guid opInstanceId)
    {
        Config.LastOpsForWindows[title] = opInstanceId;
    }
}
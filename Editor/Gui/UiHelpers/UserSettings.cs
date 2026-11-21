#nullable enable
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.Settings;
using T3.Editor.Compilation;
using T3.Editor.Gui.Windows;
using T3.Editor.Gui.Windows.RenderExport;
using T3.Editor.Gui.Windows.TimeLine;

using T3.Editor.Gui.Windows.OutputSetup;
using T3.Editor.Collab;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Saves view layout, currently open node and other user settings 
/// </summary>
///  todo - make internal, make extendable by external packages
public sealed class UserSettings : Settings<UserSettings.ConfigData>
{
    /// <summary>Sync-source values for <see cref="ConfigData.BoygroupClockSync"/>.</summary>
    public const string SyncInternal = "Internal";
    public const string SyncPtp = "PTP";

    internal UserSettings(bool saveOnQuit) : base("userSettings.json", saveOnQuit: saveOnQuit)
    {
        MigrateSharedNetworkInterface();
        RemoveDuplicateProjectDirectories();
        RemoveDuplicateBoygroupGroups();
    }

    protected override void OnBeforeSave()
    {
        RemoveDuplicateProjectDirectories();
        RemoveDuplicateBoygroupGroups();
    }

    /// <summary>
    /// One-time migration for setups configured before the session services got their own adapter
    /// selectors: the shared NetworkInterface seeds the per-service fields and is cleared, so the
    /// services keep their binding and empty (Automatic) stays a real choice afterwards.
    /// </summary>
    private static void MigrateSharedNetworkInterface()
    {
        if (string.IsNullOrWhiteSpace(Config.NetworkInterface))
            return;

        var shared = Config.NetworkInterface;
        if (string.IsNullOrWhiteSpace(Config.BoygroupInterface))
            Config.BoygroupInterface = shared;

        if (string.IsNullOrWhiteSpace(Config.CollabInterface))
            Config.CollabInterface = shared;

        if (string.IsNullOrWhiteSpace(Config.FailoverInterface))
            Config.FailoverInterface = shared;

        Config.NetworkInterface = "";
    }

    /// <summary>
    /// Repeated server starts used to append the same group over and over, leaving the panel with a
    /// column of identical "Main" rows. Strip duplicates and blanks on load and save.
    /// </summary>
    private static void RemoveDuplicateBoygroupGroups()
    {
        var groups = Config.BoygroupGroups;
        if (groups == null || groups.Count < 2)
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keptCount = 0;
        for (var index = 0; index < groups.Count; index++)
        {
            var name = groups[index].Trim();
            if (name.Length == 0 || !seen.Add(name))
                continue;

            groups[keptCount++] = name;
        }

        if (keptCount < groups.Count)
            groups.RemoveRange(keptCount, groups.Count - keptCount);

        if (groups.Count == 0)
            groups.Add("Main");
    }

    /// Duplicated entries make project loading fail on the next start, so they are stripped on load and save.
    private static void RemoveDuplicateProjectDirectories()
    {
        var directories = Config.ProjectDirectories;
        if (directories == null || directories.Count < 2)
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keptCount = 0;
        for (var index = 0; index < directories.Count; index++)
        {
            var path = directories[index].Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!seen.Add(path))
                continue;

            directories[keptCount++] = directories[index];
        }

        directories.RemoveRange(keptCount, directories.Count - keptCount);
    }

    /// <summary>
    /// Initializes gated debug logging based on current user settings configuration.
    /// </summary>
    public static void InitializeGatedLogging()
    {
        Log.Gated.Initialize(
            Config.LogAudioDetails,
            Config.LogAudioRenderingDetails,
            Config.LogVideoRenderingDetails);
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

        /// <summary>
        /// Operator bookmarks per project, keyed by the project's root symbol. The index is the digit key; each entry
        /// is an instance path of Guids joined by '/', stored as text because Guid lists don't deserialize reliably.
        /// </summary>
        public readonly Dictionary<Guid, string?[]> OperatorBookmarks = new();

        [JsonConverter(typeof(StringEnumConverter))]
        public GraphHoverModes HoverMode = GraphHoverModes.LastValue;

        // Projects
        public List<string> ProjectDirectories = [];
        public bool EnableUsbProjectDetection = true;
        public bool ReopenLastProjectOnStartup = true;

        /// <summary>Projects switched away from stay loaded for a quick return; older ones are unloaded.</summary>
        public int KeepLoadedProjectCount = 3;

        /// <summary>
        /// Share of the GPU memory budget the driver grants TiXL. Above it, render targets nothing shows are
        /// released; below it, nothing is, so a show that fits never pays for a rebuild.
        /// </summary>
        public int GpuMemoryLimitPercent = 90;

        /// <summary>0 keeps them for as long as their project is loaded, which avoids rebuild hitches.</summary>
        public int ReleaseUnusedRenderTargetsAfterSeconds = 0;

        /// <summary>
        /// Set when the user confirms the laser safety warning shown the first time the LASER
        /// master button is armed. While false, arming opens the warning dialog first.
        /// </summary>
        public bool LaserSafetyWarningAcknowledged = false;

        /// <summary>Folder of the project that was open when the editor closed; empty if it closed in the project hub.</summary>
        public string LastOpenProjectFolder = string.Empty;

        public readonly ToastSettings Toasts = new();

        // Startup & recovery
        /// <summary>Project folder that opens automatically on the next launch. Empty starts with the Hub.</summary>
        public string StartupProjectFolder = "";

        /// <summary>Installs a per-user OS login entry (Windows Run key, Linux XDG autostart, macOS LaunchAgent).</summary>
        /// <summary>Master switch for the Startup panel: project autoload, login autostart and
        /// crash restart only act while this is on. Disabled by default: an editor is a plain
        /// editor until startup automation is switched on.</summary>
        public bool StartupEnabled = false;

        public bool AutostartWithSystem = false;

        /// <summary>Relaunch the editor automatically after a crash via a supervisor process.</summary>
        public bool CrashAutoRestart = false;

        // UI-Elements
        public bool ShowThumbnails = true;
        public bool ShowMainMenu = true;
        public bool ShowTitleAndDescription = true;
        public bool ShowToolbar = true;
        public bool ShowTimeline = true;
        public bool TimelineFollowsPlayback = true;
        public bool ShowMiniMap = false;
        public bool ShowInteractionOverlay = false;
        public float LayerHeight = 18;

        /// <summary>When true, the Welcome window opens automatically at startup. Toggled from that window.</summary>
        public bool ShowWelcomeOnStartup = true;

        /// <summary>When true, a warning lists symbols with operators that could not be found, once after startup.</summary>
        public bool ShowMissingSymbolsWarningOnStartup = true;

        /// <summary>Whether the next launch offers crashes that were never reported.</summary>
        public bool AskToSendCrashReports = true;

        // UI-State
        public float UiScaleFactor = 1;
        public bool FullScreen = false;
        public int WindowLayoutIndex = 0;
        public bool SaveWindowLayoutsWithProjects = true;
        public bool EnableIdleMotion = true;
        public bool SuspendRenderingWhenHidden = true;
        public bool UseVSync = true;

        /// <summary>Caps the short side of outputs sized by their window ("Fill" and aspect presets), e.g. in focus mode on high-res displays.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public WindowFillResolutionLimits MaxWindowFillResolution = WindowFillResolutionLimits.Unlimited;

        /** Frames per second to render at with vsync; 0 renders every refresh of the pacing display. */
        public int TargetFrameRate = 0;

        /** Frames the loop may run ahead of the display: 1 for the lowest input latency, 2 to absorb late frames. */
        public int FrameLatency = 1;

        public bool EnableKeyboardShortCuts = true;

        /** Whether a connected game controller drives operators like [Gamepad] or the editor (its camera). */
        [JsonConverter(typeof(StringEnumConverter))]
        public InputDeviceUsages GamepadUsage = InputDeviceUsages.Operators;

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

        public bool UseTouchPadPanning = false;
        public float PanSpeed = 20;

        /// <summary>Opacity of a surface's content previewed over its photo — on the traced quad and on its Board card.</summary>
        public float OutputSetupContentPreviewOpacity = 0.65f;

        /// <summary>Whether the output setup's rect fields read in pixels of what they sit on, or as ratios of it.
        /// The values are stored as ratios either way; this is only how they are shown and typed.</summary>
        public OutputSetupEditUnits OutputSetupEditUnits = OutputSetupEditUnits.Pixels;
        /// <summary>The calibration-photo disc around a reference point, as a share of the canvas height.</summary>
        public float OutputSetupPhotoDiscRadius = 0.05f;

        // Mag Graph
        public bool DisconnectOnUnsnap = true;
        public float MaxCurveRadius = 350;
        public int MaxSegmentCount = 32;
        public bool EnableHorizontalSnapping = true;

        /// <summary>
        /// Below this smoothed mouse speed (screen px/s) dragging ops against a section
        /// border grows the frame, and dragging a border pushes neighbors. 0 disables.
        /// </summary>
        public float SectionSlowResizeSpeed = 20;

        public bool AddSpacesToParameterNames = true;

        public float ClickThreshold = 5; // Increase for high-res display and pen tablets
        public bool AdjustCameraSpeedWithMouseWheel = false;
        public float CameraSpeed = 1;

        public bool MiddleMouseButtonZooms = false;

        public FrameStepAmount FrameStepAmount = FrameStepAmount.FrameAt30Fps;

        public bool MouseWheelEditsNeedCtrlKey = true;
        public bool AutoPinAllAnimations = false;

        public readonly float KeyboardScrollAcceleration = 2.5f;

        public bool VariationLiveThumbnails = false;
        public bool VariationHoverPreview = true;

        public bool EditorHoverPreview = true;

        public bool HelpHoverPreview = true;

        public bool ApplyDropdownValuesOnHover = true;

        public bool ShowSkillQuestInHub = true;
        public bool SkillMapShowPlannedTopics = false;
        public bool SkillMapHighlightNewTopics = false;

        /// <summary>Checkout of the skillquest content repository. Empty = auto-discover a "skillquest" folder next to the TiXL checkout.</summary>
        public string SkillQuestRepositoryPath = string.Empty;

        // Asset Lib
        public bool SyncWithOperatorSelection = true;
        public bool ScrollAssetLibToActive = true;
        
        // Load Save
        public string UserName = UndefinedUserName;
        public bool EnableAutoBackup = true;
        public bool MinimalBackup = true;

        /// <summary>The shared network adapter of setups configured before the per-service selectors;
        /// migrated into the per-service fields on load and kept only so older files load cleanly.</summary>
        public string NetworkInterface = "";

        /// <summary>The adapter boygroup hosting and LAN discovery bind to (Settings → Networking).
        /// Empty = all interfaces.</summary>
        public string BoygroupInterface = "";

        /// <summary>The adapter collaboration hosting binds to (Settings → Networking).
        /// Empty = all interfaces.</summary>
        public string CollabInterface = "";

        /// <summary>The adapter failover heartbeats bind to (Settings → Networking).
        /// Empty = all interfaces.</summary>
        public string FailoverInterface = "";

        // Collaboration
        /// <summary>Persistent participant id; also seeds the per-user accent color.</summary>
        public string CollabUserId = "";

        /// <summary>Display name override. Falls back to the Discord name, then the OS user name.</summary>
        public string CollabDisplayName = "";

        /// <summary>Master switch: collaboration sessions can be hosted or joined. When off, no
        /// session is hosted or joined and nothing is discovered on the network. Disabled by default:
        /// a show machine does nothing on the network until it is switched on.</summary>
        public bool CollabEnabled = false;

        public int CollabPort = CollabProtocol.DefaultTcpPort;
        public bool CollabLanDiscovery = true;
        public string CollabPassword = "";

        /// <summary>The participant's own cursor color as 0xRRGGBB. 0 = derive it from the user id.</summary>
        public int CollabCursorColor = 0;

        /// <summary>host:port of the relay used for room-code sessions. Empty = default relay.</summary>

        public string CollabRelayAddress = "";

        /// <summary>Relay room join anchor: rejoin after a restart (e.g. a project download)
        /// comes back through the same room. Cleared on leave and by a direct-session join.</summary>
        public string CollabLastRelayHost = "";
        public string CollabLastRelayRoom = "";

        /// <summary>Relay sessions authenticate over the relay, then try a direct WebRTC path between
        /// the machines. Falls back to the relay when no direct path is possible.</summary>
        public bool CollabWebRtcEnabled = true;

        /// <summary>Streams the host's rendered output to the participants over the peer-to-peer
        /// video track ("Share preview"). Applies to peers that join afterwards.</summary>
        public bool CollabSharePreview = true;

        /// <summary>Comma-separated STUN/TURN URLs for the direct path. Empty = a public STUN server.</summary>
        public string CollabWebRtcIceServers = "";

        /// <summary>Project folders participating in show-control features ("kind:folder" entries,
        /// kind = boygroup | collab). The Hub lists them under "Network projects".</summary>
        public List<string> NetworkProjectRefs = [];

        /// <summary>host:port of the last joined collab session — auto-rejoined on launch, which is
        /// what brings a client back after a project download restarted the editor.</summary>
        public string CollabLastSessionHost = "";

        public string CollabDiscordClientId = "";
        public string CollabDiscordClientSecret = "";
        public string CollabDiscordUserId = "";
        public string CollabDiscordUserName = "";
        public string CollabDiscordUserAvatar = "";

        /// <summary>Language tag of the Discord profile ("hu", "en-US"); shown next to the name in sessions.</summary>
        public string CollabDiscordLocale = "";

        /// <summary>Keeps the Discord token in this file so the account stays confirmed to other
        /// participants across restarts. Off: the token lives in memory only and a restart needs a new login.</summary>
        public bool CollabDiscordStayLoggedIn = false;

        public string CollabDiscordAccessToken = "";
        public string CollabDiscordTokenExpiresUtc = "";

        /// <summary>Title shown in the relay's session list when hosting.</summary>
        public string CollabSessionTitle = "";

        /// <summary>What the session is for (Jam, Show, Teaching, Help); shown in the session list.</summary>
        public string CollabSessionPurpose = "Jam";

        /// <summary>Lists a hosted relay session in the relay's session browser. Off: only people with the session ID find it.</summary>
        public bool CollabListSession = false;

        /// <summary>Bot token that lets this editor post and read session cards in a Discord lobby channel (serverless session browser).</summary>
        public string CollabDiscordBotToken = "";

        /// <summary>The Discord channel whose messages are the session browser (a forum post channel or a normal text channel).</summary>
        public string CollabDiscordLobbyChannelId = "";

        /// <summary>Base URL of a self-hosted collab backend (CollabBackend/api.php): the session browser, whispers and WebRTC signaling.</summary>
        public string CollabBackendUrl = "";

        /// <summary>Party form: Online (through the backend server) when true, Local (LAN) when false.</summary>
        public bool CollabPartyOnline = true;

        /// <summary>Party form: Controllable (participants may edit) when true, Spectating when false.</summary>
        public bool CollabPartyControllable = true;

        /// <summary>The backend party room this editor last joined, so a restart (e.g. after receiving
        /// the host's project) rejoins the same party automatically.</summary>
        public string CollabLastBackendRoom = "";

        // Remote management (the Collab window's Remote tab). The installation keeps a device id
        // and secret issued when the owner's 6-digit code was redeemed, so it needs no Discord
        // login; the owner sees its computers through the backend instead of this file.
        /// <summary>Name shown to the owner for this computer. Empty = the machine name.</summary>
        public string CollabRemoteName = "";

        /// <summary>Id of this installation in the remote registry (set by pairing).</summary>
        public string CollabRemoteDeviceId = "";

        /// <summary>Long-lived secret that authenticates this installation's heartbeats and reports.</summary>
        public string CollabRemoteDeviceKey = "";

        /// <summary>Display name of the Discord account this installation is paired with.</summary>
        public string CollabRemoteOwner = "";

        /// <summary>Local consent switch: the installation only heartbeats and accepts a managed
        /// session while this is on. Pairing turns it on; it can be turned off at any time.</summary>
        public bool CollabRemoteAllow = false;

        // Boygrouping (show distribution to render nodes)
        /// <summary>Master switch: this machine participates in boygrouping. When set, the role
        /// below starts automatically shortly after launch — a show machine comes up in its role
        /// without anyone clicking.</summary>
        public bool BoygroupEnabled = false;

        /// <summary>true = this machine is a master (server); false = slave (render node).</summary>
        public bool BoygroupIsMaster = false;

        public string BoygroupClientName = "";
        public int BoygroupPort = 44720;
        public bool BoygroupAutoJoin = true;
        public bool BoygroupDeployMirror = false;
        /// <summary>The master deploys and syncs to a render node as soon as it joins the group,
        /// so an added slave is current without anyone clicking Deploy &amp; Sync.</summary>
        public bool BoygroupAutoDeployOnJoin = true;

        /// <summary>Where a render node takes its playback clock from. Internal = TiXL's own
        /// NTP-style exchange over the session link (default, needs nothing). Ptp = the OS clock is
        /// trusted because something else (a PTP daemon, e.g. linuxptp/phc2sys or w32time with a
        /// PTP source) disciplines it — anchors then apply with near-zero offset error.</summary>
        public string BoygroupClockSync = "Internal";

        /// <summary>Deploy-restart flood guard, persisted across the restarts it guards: if more
        /// than four deploy restarts happen inside two minutes the client stops restarting for
        /// five minutes and logs loudly, so a perpetually-diffing file can never loop a show
        /// machine.</summary>
        public int BoygroupDeployRestartCount = 0;
        public long BoygroupDeployRestartWindowStartTicks = 0;
        public long BoygroupDeployRestartBlockedUntilTicks = 0;

        public List<string> BoygroupGroups = ["Main"];
        public string BoygroupLastServer = "";
        public string BoygroupLastGroup = "";

        // Show failover (active/standby output arbitration)
        public bool FailoverEnabled = false;
        public string FailoverShowId = "tixl-show";
        public int FailoverPriority = 100;
        public int FailoverHeartbeatMs = 500;
        public int FailoverTimeoutFactor = 3;
        public bool FailoverPreempt = true;

        public float GizmoSize = 100;


        /// <summary>Seconds between captures while continuous screenshot mode is active (Ctrl-click the output's screenshot icon).</summary>
        public float ContinuousScreenshotDelay = 5;

        // FileFormats is internal, so this field must be too; JsonProperty keeps it serialized.
        /// <summary>File format used by the output window's screenshot icon. JPG produces much smaller files for sequences.</summary>
        [JsonProperty]
        [JsonConverter(typeof(StringEnumConverter))]
        internal ScreenshotWriter.FileFormats ScreenshotFileFormat = ScreenshotWriter.FileFormats.Png;

        // Timeline
        public float TimeRasterDensity = 1f;

        // Space mouse
        public float SpaceMouseRotationSpeedFactor = 0.3f;
        public float SpaceMouseMoveSpeedFactor = 0.2f;
        public float SpaceMouseDamping = 0.95f;

        /** Whether the SpaceMouse flies the camera (Editor) or drives operators like [SpaceMouse]. */
        [JsonConverter(typeof(StringEnumConverter))]
        public InputDeviceUsages SpaceMouseUsage = InputDeviceUsages.Editor;

        // Game controller camera
        /** Seconds the camera takes to follow the sticks; longer is calmer, e.g. for live demos. */
        public float GamepadCameraSmoothing = 0.4f;

        public float GamepadCameraMoveSpeed = 1f;
        public float GamepadCameraLookSpeed = 1f;
        public bool GamepadCameraInvertLook;

        // Migration-only: these load from existing userSettings.json to preserve
        // the user's last render path counters. Not written back.
        [Obsolete("Migrated to per-symbol RenderSettings in .t3ui")]
        public string? RenderVideoFilePath;
        [Obsolete("Migrated to per-symbol RenderSettings in .t3ui")]
        public string? RenderSequenceFilePath;
        [Obsolete("Migrated to per-symbol RenderSettings in .t3ui")]
        public string? RenderSequenceFileName;
        [Obsolete("Migrated to per-symbol RenderSettings in .t3ui")]
        public string? RenderSequencePrefix;

        // Video-proxy preferences moved to per-project CompositionSettings.ProxyConfig (they travel with the
        // project) — ProxyFormat / ProxyResolution / UseProxyVideos are no longer per-machine user settings.
        // This one stays per-machine: it guards the local disk against proxy generation when space is low.
        public float ProxyMinFreeDiskGb = 5f; // refuse to generate a proxy if the target drive has less free space

        // Profiling and debugging
        public bool EnableFrameProfiling = true;
        public bool KeepTraceForLogMessages = false;
        public bool EnableGCProfiling = false;
        public bool EnableMidiDebugLogging = false;
        public bool ShowOperatorStats = false;
        public int DebugServerPort = 9042;
        
        // Gated Debug Logging
        public bool LogAudioDetails = false;
        public bool LogAudioRenderingDetails = false;
        public bool LogVideoRenderingDetails = false;
        public bool LogPresentationModeChanges = false;

        public CompilerOptions.Verbosity CompileCsVerbosity = CompilerOptions.Verbosity.Minimal;

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

        // Rendering Profiling
        public bool ShowRenderProfilingLogs = false;

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

    /** Values are the largest short side in pixels; 0 means no limit. */
    public enum WindowFillResolutionLimits
    {
        Unlimited = 0,
        Max720p = 720,
        Max1080p = 1080,
        Max1440p = 1440,
        Max2160p = 2160,
    }

    /** Who an input device drives; Editor means the camera of the output. Stored by name. */
    public enum InputDeviceUsages
    {
        Operators,
        Editor,
        Off,
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
    // Rendering Profiling
}
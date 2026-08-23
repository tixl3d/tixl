using System;

namespace T3.Core.IO;

/// <summary>
/// Global application settings shared across Core, Editor and Player.
/// Saved to projectSettings.json in the settings directory.
/// </summary>
public sealed class CoreSettings : Settings<CoreSettings.ConfigData>
{
    public CoreSettings(bool saveOnQuit) : base("projectSettings.json", saveOnQuit)
    {
    }

    public sealed class ConfigData
    {
        public bool LogAssemblyVersionMismatches = false;

        public string LimitMidiDeviceCapture = null;

        // Escape hatch: revert to pre-4.3 per-process shadow copies (fresh copy of every editable
        // package on each editor start) in case the shared content-keyed cache misbehaves.
        public bool UseProcessScopedShadowCopies = false;

        // Logging
        public bool LogCompilationDetails = false;
        public bool LogAssemblyLoadingDetails = false;
        public bool LogFileEvents = false;

        // Audio
        public bool AppMute = false;
        public float AppVolume = 1;

        /// <summary>
        /// Machine-specific WASAPI input device used when a project leaves its AudioInputDeviceName
        /// empty ("use default input"). Kept out of the project file so shared projects stay portable.
        /// </summary>
        public string LocalAudioInputDeviceName = string.Empty;

        // IO
        public int DefaultOscPort = 8000;

        // Performance
        public bool TimeClipSuspending = true;
        public bool SkipOptimization;
        public bool EnableDirectXDebug;
        public bool EnableBeatSyncProfiling = false;
    }
}

/// <summary>
/// Written by the editor next to an exported Player.exe and read by the player on startup.
/// <paramref name="Export"/> carries the project's startup defaults (window mode, resolution, dialog...).
/// </summary>
[Serializable]
public record ExportSettings(Guid OperatorId,
                             string ApplicationTitle,
                             string Author,
                             Guid BuildId,
                             string EditorVersion,
                             Settings.CompositionSettings.ExportConfig Export,
                             CoreSettings.ConfigData ConfigData)
{
    public const string FileName = "exportSettings.json";
}

public enum WindowMode { Windowed, Fullscreen }

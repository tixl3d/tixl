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

        // Logging
        public bool LogCompilationDetails = false;
        public bool LogAssemblyLoadingDetails = false;
        public bool LogFileEvents = false;

        // Audio
        public bool AppMute = false;
        public float AppVolume = 1;

        // IO
        public int DefaultOscPort = 8000;

        // Performance
        public bool TimeClipSuspending = true;
        public bool SkipOptimization;
        public bool EnableDirectXDebug;
        public bool EnableBeatSyncProfiling = false;
    }
}

[Serializable]
public record ExportSettings(Guid OperatorId, string ApplicationTitle, WindowMode WindowMode, CoreSettings.ConfigData ConfigData, string Author, Guid BuildId, string EditorVersion);

public enum WindowMode { Windowed, Fullscreen }

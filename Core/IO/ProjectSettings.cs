using System;

namespace T3.Core.IO;

/// <summary>
/// Saves view layout and currently open node 
/// </summary>
public class ProjectSettings : Settings<ProjectSettings.ConfigData>
{
    public ProjectSettings(bool saveOnQuit) : base("projectSettings.json", saveOnQuit)
    {
    }
        
    public class ConfigData
    {
        public bool TimeClipSuspending = true;
        public float AudioResyncThreshold = 0.04f;
        public bool EnablePlaybackControlWithKeyboard = true;

        public bool SkipOptimization;
        public bool EnableDirectXDebug;
        
        public bool LogAssemblyVersionMismatches = false;
            
        public string LimitMidiDeviceCapture = null; 
        public bool EnableMidiSnapshotIndication = false;
        public WindowMode DefaultWindowMode = WindowMode.Fullscreen;
        public int DefaultOscPort = 8000;

        
        
        // Profiling
        public bool EnableBeatSyncProfiling = false;
        public float BeatSyncOffsetMs = 0;
    }
}

[Serializable]
public record ExportSettings(Guid OperatorId, string ApplicationTitle, WindowMode WindowMode, ProjectSettings.ConfigData ConfigData, string Author, Guid BuildId, string EditorVersion);
    
public enum WindowMode { Windowed, Fullscreen }
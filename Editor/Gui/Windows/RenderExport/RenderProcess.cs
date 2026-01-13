#nullable enable
using System.IO;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport.MF;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

/// <summary>
/// Provides static methods and state for managing the rendering and export process in the editor.
/// Handles starting, updating, and cancelling render sessions for video and image sequence exports,
/// as well as managing output textures, resolutions, and export progress.
/// </summary>
internal static class RenderProcess
{
    public static string LastHelpString { get; private set; } = string.Empty;
    public static string LastTargetDirectory { get; private set; } = string.Empty;


    
    public static Type? MainOutputType { get; private set; }
    public static Int2 MainOutputOriginalSize;
    public static Int2 MainOutputRenderedSize;
    public static Texture2D? MainOutputTexture;
    
    public static States State;

    // TODO: clarify the difference
    public static bool IsExporting { get; private set; }
    public static bool IsToollRenderingSomething { get; private set; }
    

    
    /// <summary>
    /// Represents the current state of the render process.
    /// </summary>
    public enum States
    {
        /// <summary>No output window is available.</summary>
        NoOutputWindow,
        /// <summary>The output type is not valid for rendering.</summary>
        NoValidOutputType,
        /// <summary>The output texture is not valid or missing.</summary>
        NoValidOutputTexture,
        /// <summary>Ready and waiting for export to start.</summary>
        WaitingForExport,
        /// <summary>Currently exporting frames.</summary>
        Exporting,
    }

    /// <summary>
    /// Updates the render process state. Should be called once per frame.
    /// </summary>
    public static void Update()
    {
        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            State = States.NoOutputWindow;
            return;
        }

        MainOutputTexture = outputWindow.GetCurrentTexture();
        if (MainOutputTexture == null)
        {
            State = States.NoValidOutputTexture;
            return;
        }

        MainOutputType = outputWindow.ShownInstance?.Outputs.FirstOrDefault()?.ValueType;
        if (MainOutputType != typeof(Texture2D))
        {
            State = States.NoValidOutputType;
            return;
        }

        HandleRenderShortCuts();

        if (!IsExporting)
        {
            var baseResolution = outputWindow.GetResolution();
            MainOutputOriginalSize = baseResolution;

            MainOutputRenderedSize = new Int2(((int)(baseResolution.Width * RenderSettings.Current.ResolutionFactor) / 2 * 2).Clamp(2,16384),
                                              ((int)(baseResolution.Height * RenderSettings.Current.ResolutionFactor) / 2 * 2).Clamp(2,16384));
            
            State = States.WaitingForExport;
            return;
        }

        if (_activeSession == null) return;
        
        State = States.Exporting;

        // Process frame
        bool success;
        if (_activeSession.Settings.RenderMode == RenderSettings.RenderModes.Video)
        {
            var audioFrame = AudioRendering.GetLastMixDownBuffer(1.0 / _activeSession.Settings.Fps);
            success = SaveVideoFrameAndAdvance( ref audioFrame, RenderAudioInfo.SoundtrackChannels(), RenderAudioInfo.SoundtrackSampleRate());
        }
        else
        {
            AudioRendering.GetLastMixDownBuffer(Playback.LastFrameDuration);
            success = SaveImageFrameAndAdvance();
        }
        
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (_activeSession == null) 
            return;

        // Update stats
        var effectiveFrameCount = _activeSession.Settings.RenderMode == RenderSettings.RenderModes.Video ? _activeSession.FrameCount : _activeSession.FrameCount + 2;
        var currentFrame = _activeSession.Settings.RenderMode == RenderSettings.RenderModes.Video ? GetRealFrame() : _activeSession.FrameIndex + 1;

        var completed = currentFrame >= effectiveFrameCount || !success;
        if (!completed) 
            return;

        var duration = Playback.RunTimeInSecs - _activeSession.ExportStartedTime;
        var successful = success ? "successfully" : "unsuccessfully";
        LastHelpString = $"Render {GetTargetFilePath(_activeSession.Settings.RenderMode)} finished {successful} in {StringUtils.HumanReadableDurationFromSeconds(duration)}";
        Log.Debug(LastHelpString);

        if (success)
        {
            if (_activeSession.Settings.RenderMode == RenderSettings.RenderModes.Video && _activeSession.Settings.AutoIncrementVersionNumber)
            {
                RenderPaths.TryIncrementVideoFileNameInUserSettings();
            }
            else if (_activeSession.Settings.RenderMode == RenderSettings.RenderModes.ImageSequence && _activeSession.Settings.AutoIncrementSubFolder)
            {
                if (_activeSession.Settings.CreateSubFolder)
                {
                    UserSettings.Config.RenderSequenceFileName = RenderPaths.GetNextIncrementedPath(UserSettings.Config.RenderSequenceFileName);
                }
                else
                {
                    UserSettings.Config.RenderSequencePrefix = RenderPaths.GetNextIncrementedPath(UserSettings.Config.RenderSequencePrefix);
                }
                UserSettings.Save();
            }
        }

        Cleanup();
        IsToollRenderingSomething = false;
    }
    
    /// <summary>
    /// Handles keyboard shortcuts for rendering actions such as starting, cancelling, or taking a screenshot.
    /// </summary>
    private static void HandleRenderShortCuts()
    {
        if (MainOutputTexture == null)
            return;

        if (UserActions.RenderAnimation.Triggered())
        {
            if (IsExporting)
            {
                Cancel();
            }
            else
            {
                TryStart(RenderSettings.Current);
            }
        }

        if (UserActions.RenderScreenshot.Triggered())
        {
            TryRenderScreenShot();
        }
    }

    /// <summary>
    /// Starts a new render export session with the specified settings, if not already exporting.
    /// </summary>
    /// <param name="renderSettings">The settings to use for the render export session.</param>
    public static void TryStart(RenderSettings renderSettings)
    {
        if (IsExporting)
        {
            Log.Warning("Export is already in progress");
            return;
        }

        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            Log.Warning("No output window found to start export");
            return;
        }
        
        var targetFilePath = GetTargetFilePath(renderSettings.RenderMode);
        
        if (!RenderPaths.ValidateOrCreateTargetFolder(targetFilePath))
            return;

        // Start new session
        _activeSession = new ExportSession
                         {
                             Settings = renderSettings,
                             FrameCount = RenderTiming.ComputeFrameCount(renderSettings),
                             ExportStartedTime = Playback.RunTimeInSecs,
                             ExportStartTimeLocal = Core.Animation.Playback.RunTimeInSecs,
                             FrameIndex = 0,
                         };

        // Lock the resolution at the start of export
        var baseResolution = outputWindow.GetResolution();
        MainOutputOriginalSize = baseResolution;
        MainOutputRenderedSize = new Int2(
            ((int)(baseResolution.Width * _activeSession.Settings.ResolutionFactor) / 2 * 2).Clamp(2, 16384),
            ((int)(baseResolution.Height * _activeSession.Settings.ResolutionFactor) / 2 * 2).Clamp(2, 16384)
        );

        _activeSession.FrameCount = Math.Max(_activeSession.FrameCount, 0);

        IsToollRenderingSomething = true;


        if (_activeSession.Settings.RenderMode == RenderSettings.RenderModes.Video)
        {
            _activeSession.VideoWriter = new Mp4VideoWriter(targetFilePath, MainOutputOriginalSize, MainOutputRenderedSize, _activeSession.Settings.ExportAudio)
                               {
                                   Bitrate = _activeSession.Settings.Bitrate,
                                   Framerate = (int)_activeSession.Settings.Fps
                               };
        }
        else
        {
            var directory = Path.GetDirectoryName(targetFilePath);
            _activeSession.TargetFolder = directory ?? targetFilePath;
        }

        LastTargetDirectory = _activeSession.TargetFolder;
        if (_activeSession.Settings.RenderMode == RenderSettings.RenderModes.Video)
        {
            LastTargetDirectory = Path.GetDirectoryName(targetFilePath) ?? string.Empty;
        }

        ScreenshotWriter.ClearQueue();

        // set playback to the first frame
        RenderTiming.SetPlaybackTimeForFrame(ref _activeSession.Settings, _activeSession.FrameIndex, _activeSession.FrameCount, ref _activeSession.Runtime);
        IsExporting = true;
        LastHelpString = "Rendering...";
    }

    /// <summary>
    /// Cancels the current render export session, if any, and updates the help string with an optional reason.
    /// </summary>
    /// <param name="reason">Optional reason for cancellation.</param>
    public static void Cancel(string? reason = null)
    {
        if (_activeSession == null) return;
        var duration = Playback.RunTimeInSecs - _activeSession.ExportStartedTime;
        LastHelpString = reason ?? $"Render cancelled after {StringUtils.HumanReadableDurationFromSeconds(duration)}";
        Cleanup();
        IsToollRenderingSomething = false;
    }

    /// <summary>
    /// Cleans up the current export session and releases resources.
    /// </summary>
    private static void Cleanup()
    {
        IsExporting = false;

        if (_activeSession != null)
        {
            if (_activeSession.Settings.RenderMode == RenderSettings.RenderModes.Video)
            {
                _activeSession.VideoWriter?.Dispose();
            }

            RenderTiming.ReleasePlaybackTime(ref _activeSession.Settings, ref _activeSession.Runtime);
            _activeSession = null;
        }
    }

    /// <summary>
    /// Saves the current video frame and advances the export session, handling audio and resolution mismatches.
    /// </summary>
    /// <param name="audioFrame">Reference to the audio frame buffer.</param>
    /// <param name="channels">Number of audio channels.</param>
    /// <param name="sampleRate">Audio sample rate.</param>
    /// <returns>True if the frame was processed successfully; otherwise, false.</returns>
    private static bool SaveVideoFrameAndAdvance(ref byte[] audioFrame, int channels, int sampleRate)
    {
        if (Playback.OpNotReady)
        {
            Log.Debug("Waiting for operators to complete");
            return true;
        }

        try
        {
            // Explicitly check for resolution mismatch BEFORE calling video writer
            // This prevents passing bad frames to the writer and allows us to handle the "wait" logic here
            var texture = MainOutputTexture;
            if (texture == null)
            {
                Log.Warning("[SaveVideoFrameAndAdvance] Main output texture is null during export");
                return false;
            }
            
            var currentDesc = texture.Description;
            if (currentDesc.Width != MainOutputRenderedSize.Width || currentDesc.Height != MainOutputRenderedSize.Height)
            {
                _activeSession!.ResolutionMismatchCount++;
                if (_activeSession.ResolutionMismatchCount > MaxResolutionMismatchRetries)
                {
                    Log.Warning($"Resolution mismatch timed out after {_activeSession.ResolutionMismatchCount} frames ({currentDesc.Width}x{currentDesc.Height} vs {MainOutputRenderedSize.Width}x{MainOutputRenderedSize.Height}). Forcing advance.");
                    _activeSession.FrameIndex++;
                    RenderTiming.SetPlaybackTimeForFrame(ref _activeSession.Settings, _activeSession.FrameIndex, _activeSession.FrameCount, ref _activeSession.Runtime);
                    _activeSession.ResolutionMismatchCount = 0;
                }
                else
                {
                    // Stay on same frame, wait for engine to resize
                    // Log.Debug($"Waiting for resolution match... ({currentDesc.Width}x{currentDesc.Height} vs {MainOutputRenderedSize.Width}x{MainOutputRenderedSize.Height})");
                }
                return true;
            }

            // Resolution matches, proceed with write and advance
            _activeSession!.ResolutionMismatchCount = 0;
            _activeSession.VideoWriter?.ProcessFrames( MainOutputTexture, ref audioFrame, channels, sampleRate);
            
            _activeSession.FrameIndex++;
            RenderTiming.SetPlaybackTimeForFrame(ref _activeSession.Settings, _activeSession.FrameIndex, _activeSession.FrameCount, ref _activeSession.Runtime);
            
            return true;
        }
        catch (Exception e)
        {
            LastHelpString = e.ToString();
            Cleanup();
            return false;
        }
    }

    /// <summary>
    /// Returns the file path for the current image sequence frame.
    /// </summary>
    /// <returns>The file path for the image sequence frame.</returns>
    private static string GetSequenceFilePath()
    {
        var prefix = RenderPaths.SanitizeFilename(UserSettings.Config.RenderSequencePrefix);
        return Path.Combine(_activeSession!.TargetFolder, $"{prefix}_{_activeSession.FrameIndex:0000}.{_activeSession.Settings.FileFormat.ToString().ToLower()}");
    }

    /// <summary>
    /// Saves the current image frame and advances the export session.
    /// </summary>
    /// <returns>True if the frame was saved successfully; otherwise, false.</returns>
    private static bool SaveImageFrameAndAdvance()
    {
        if (MainOutputTexture == null)
            return false;
        
        try
        {
            var success = ScreenshotWriter.StartSavingToFile(MainOutputTexture, GetSequenceFilePath(), _activeSession!.Settings.FileFormat);
            _activeSession.FrameIndex++;
            RenderTiming.SetPlaybackTimeForFrame(ref _activeSession.Settings, _activeSession.FrameIndex, _activeSession.FrameCount, ref _activeSession.Runtime);
            return success;
        }
        catch (Exception e)
        {
            LastHelpString = e.ToString();
            IsExporting = false;
            return false;
        }
    }

    /// <summary>
    /// Returns the real frame index, accounting for skipped images in video export.
    /// </summary>
    /// <returns>The real frame index.</returns>
    private static int GetRealFrame() => _activeSession!.FrameIndex - MfVideoWriter.SkipImages;

    /// <summary>
    /// Returns the target file path for the specified render mode.
    /// </summary>
    /// <param name="renderMode">The render mode (video or image sequence).</param>
    /// <returns>The target file path.</returns>
    private static string GetTargetFilePath(RenderSettings.RenderModes renderMode) => RenderPaths.GetTargetFilePath(renderMode);

    /// <summary>
    /// Attempts to render a screenshot of the current main output texture and saves it to the project folder.
    /// </summary>
    public static void TryRenderScreenShot()
    {
        if (MainOutputTexture == null) return;
        
        var project = ProjectView.Focused?.OpenedProject;
        if (project == null) return;
        
        var projectFolder = project.Package.Folder;
        var folder = Path.Combine(projectFolder, "Screenshots");            
            
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        var filename = Path.Join(folder, $"{DateTime.Now:yyyy_MM_dd-HH_mm_ss_fff}.png");
        ScreenshotWriter.StartSavingToFile(RenderProcess.MainOutputTexture, filename, ScreenshotWriter.FileFormats.Png);
        Log.Debug($"Screenshot saved at: {filename}");
    }

    /// <summary>
    /// Represents an active export session, holding state and settings for the current render/export process.
    /// </summary>
    private class ExportSession
    {
        public Mp4VideoWriter? VideoWriter;
        public string TargetFolder = string.Empty;
        public double ExportStartedTime;
        public int FrameIndex;
        public int FrameCount;
        public RenderSettings Settings = null!;
        public RenderTiming.Runtime Runtime;
        public int ResolutionMismatchCount;
        public double ExportStartTimeLocal;
    }

    private static ExportSession? _activeSession;
    private const int MaxResolutionMismatchRetries = 10;

    /// <summary>
    /// Gets the local time when the export started, or 0 if no session is active.
    /// </summary>
    public static double ExportStartedTimeLocal => _activeSession?.ExportStartTimeLocal ?? 0;

    /// <summary>
    /// Gets the progress of the current export session as a value between 0.0 and 1.0.
    /// </summary>
    public static double Progress => _activeSession == null || _activeSession.FrameCount <= 1 ? 0.0 : (_activeSession.FrameIndex / (double)(_activeSession.FrameCount - 1));
}
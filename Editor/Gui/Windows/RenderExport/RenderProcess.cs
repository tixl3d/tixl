#nullable enable
using System.IO;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Logging;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.UiHelpers;
using T3.Core.IO;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport.MF;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

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

    private static ExportSession? _activeSession;
    private static int _frameIndex;
    private static int _frameCount;
    private static RenderSettings _renderSettings = null!;
    private static RenderTiming.Runtime _runtime;
    private const int MaxResolutionMismatchRetries = 10;

    public enum States
    {
        NoOutputWindow,
        NoValidOutputType,
        NoValidOutputTexture,
        WaitingForExport,
        Exporting,
    }

    public static OutputWindow? OutputWindow;
    
    /// <remarks>
    /// needs to be called once per frame
    /// </remarks>
    public static void Update()
    {
        if (!OutputWindow.TryGetPrimaryOutputWindow(out OutputWindow))
        {
            State = States.NoOutputWindow;
            return;
        }

        MainOutputTexture = OutputWindow.GetCurrentTexture();
        if (MainOutputTexture == null)
        {
            State = States.NoValidOutputTexture;
            return;
        }

        MainOutputType = OutputWindow.ShownInstance?.Outputs.FirstOrDefault()?.ValueType;
        if (MainOutputType != typeof(Texture2D))
        {
            State = States.NoValidOutputType;
            return;
        }

        HandleRenderShortCuts();

        if (!IsExporting)
        {
            var baseResolution = OutputWindow.GetResolution();
            MainOutputOriginalSize = baseResolution;

            MainOutputRenderedSize = new Int2(((int)(baseResolution.Width * RenderSettings.Current.ResolutionFactor) / 2 * 2).Clamp(2,16384),
                                              ((int)(baseResolution.Height * RenderSettings.Current.ResolutionFactor) / 2 * 2).Clamp(2,16384));
            
            State = States.WaitingForExport;
            return;
        }

        if (_activeSession == null) return;
        
        State = States.Exporting;

        // Handle waiting for first frame at correct resolution
        if (_activeSession.WaitingForFirstFrame)
        {
            var currentDesc = MainOutputTexture.Description;
            if (currentDesc.Width == MainOutputRenderedSize.Width && currentDesc.Height == MainOutputRenderedSize.Height)
            {
                // Resolution is now correct - set playback time for frame 0
                // The OutputWindow will render frame 0 at the correct resolution in this frame's Draw()
                // We'll process it on the next Update() cycle
                RenderTiming.SetPlaybackTimeForFrame(ref _activeSession.Settings, _activeSession.FrameIndex, _activeSession.FrameCount, ref _activeSession.Runtime);
                _activeSession.WaitingForFirstFrame = false;
                _activeSession.WaitingForFirstFrameCount = 0;
            }
            else
            {
                // Increment wait count and timeout if necessary
                _activeSession.WaitingForFirstFrameCount++;
                if (_activeSession.WaitingForFirstFrameCount > MaxResolutionMismatchRetries)
                {
                    Log.Warning($"First frame resolution wait timed out ({currentDesc.Width}x{currentDesc.Height} vs {MainOutputRenderedSize.Width}x{MainOutputRenderedSize.Height}). Proceeding anyway.");
                    RenderTiming.SetPlaybackTimeForFrame(ref _activeSession.Settings, _activeSession.FrameIndex, _activeSession.FrameCount, ref _activeSession.Runtime);
                    _activeSession.WaitingForFirstFrame = false;
                    _activeSession.WaitingForFirstFrameCount = 0;
                }
            }
            // Don't process frames yet - return and let the OutputWindow render
            return;
        }

        // Process frame
        bool success;
        if (_activeSession.Settings.RenderMode == RenderSettings.RenderModes.Video)
        {
            // Use the new full mixdown buffer for audio export
            double localFxTime = _frameIndex / _renderSettings.Fps;
            Log.Gated.VideoRender($"Requested recording from {0.0000:F4} to {(_activeSession.FrameCount / _renderSettings.Fps):F4} seconds");
            Log.Gated.VideoRender($"Actually recording from {(_frameIndex / _renderSettings.Fps):F4} to {((_frameIndex + 1) / _renderSettings.Fps):F4} seconds due to frame raster");
            var audioFrameFloat = AudioRendering.GetFullMixDownBuffer(1.0 / _renderSettings.Fps);
            // Safety: ensure audioFrameFloat is valid and sized
            if (audioFrameFloat == null || audioFrameFloat.Length == 0)
            {
                Log.Error($"RenderProcess: AudioRendering.GetFullMixDownBuffer returned null or empty at frame {_frameIndex}", typeof(RenderProcess));
                int sampleRate = RenderAudioInfo.SoundtrackSampleRate();
                int channels = RenderAudioInfo.SoundtrackChannels();
                int floatCount = (int)Math.Max(Math.Round((1.0 / _renderSettings.Fps) * sampleRate), 0.0) * channels;
                audioFrameFloat = new float[floatCount]; // silence
            }
            // Convert float[] to byte[] for the writer
            var audioFrame = new byte[audioFrameFloat.Length * sizeof(float)];
            Buffer.BlockCopy(audioFrameFloat, 0, audioFrame, 0, audioFrame.Length);
            // Force metering outputs to update for UI/graph
            AudioRendering.EvaluateAllAudioMeteringOutputs(localFxTime, audioFrameFloat);
            success = SaveVideoFrameAndAdvance(ref audioFrame, RenderAudioInfo.SoundtrackChannels(), RenderAudioInfo.SoundtrackSampleRate());
        }
        else
        {
            AudioRendering.GetLastMixDownBuffer(Playback.LastFrameDuration);
            // For image export, also update metering for UI/graph
            // Use FxTimeInBars as a substitute for LocalFxTime
            AudioRendering.EvaluateAllAudioMeteringOutputs(Playback.Current.FxTimeInBars);
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

    public static void TryStart(RenderSettings renderSettings)
    {
        if (IsExporting)
        {
            Log.Warning("Export is already in progress");
            return;
        }

        // Ensure previous session is cleaned up and file handles are released
        Cleanup();

        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            Log.Warning("No output window found to start export");
            return;
        }
        
        var targetFilePath = GetTargetFilePath(renderSettings.RenderMode);
        
        if (!RenderPaths.ValidateOrCreateTargetFolder(targetFilePath))
            return;

        // If file exists, delete it to avoid file-in-use errors
        if (renderSettings.RenderMode == RenderSettings.RenderModes.Video && File.Exists(targetFilePath))
        {
            try
            {
                File.Delete(targetFilePath);
                // Optional: Give the OS a moment to release the handle
                System.Threading.Thread.Sleep(100);
            }
            catch (IOException ex)
            {
                var msg = $"The output file '{targetFilePath}' could not be deleted or is in use by another process. Please close any application using it and try again.\n{ex.Message}";
                Log.Error(msg, typeof(RenderProcess));
                LastHelpString = msg;
                IsExporting = false;
                IsToollRenderingSomething = false;
                State = States.WaitingForExport;
                return;
            }
        }

        // Start new session
        _activeSession = new ExportSession
                         {
                             Settings = renderSettings,
                             FrameCount = RenderTiming.ComputeFrameCount(renderSettings),
                             ExportStartedTime = Playback.RunTimeInSecs,
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

        _renderSettings = renderSettings;
        
        _frameIndex = 0;
        _frameCount = Math.Max(_renderSettings.FrameCount, 0);

        if (_activeSession.Settings.RenderMode == RenderSettings.RenderModes.Video)
        {
            // Log all relevant parameters before initializing video writer
            Log.Gated.VideoRender($"Initializing Mp4VideoWriter with: path={targetFilePath}, size={MainOutputOriginalSize.Width}x{MainOutputOriginalSize.Height}, renderedSize={MainOutputRenderedSize.Width}x{MainOutputRenderedSize.Height}, bitrate={_renderSettings.Bitrate}, framerate={_renderSettings.Fps}, audio={_renderSettings.ExportAudio}, channels={RenderAudioInfo.SoundtrackChannels()}, sampleRate={RenderAudioInfo.SoundtrackSampleRate()}, codec=H.264 (default for Mp4VideoWriter)");
            try
            {
                _activeSession.VideoWriter = new Mp4VideoWriter(targetFilePath, MainOutputOriginalSize, MainOutputRenderedSize, _activeSession.Settings.ExportAudio)
                {
                    Bitrate = _activeSession.Settings.Bitrate,
                    Framerate = (int)_activeSession.Settings.Fps
                };
                Log.Gated.VideoRender($"Mp4VideoWriter initialized: Codec=H.264, FileFormat=mp4, Bitrate={_activeSession.VideoWriter.Bitrate}, Framerate={_activeSession.VideoWriter.Framerate}, Channels={RenderAudioInfo.SoundtrackChannels()}, SampleRate={RenderAudioInfo.SoundtrackSampleRate()}");
            }
            catch (Exception ex)
            {
                var msg = $"Failed to initialize Mp4VideoWriter: {ex.Message}\n{ex.StackTrace}";
                Log.Error(msg, typeof(RenderProcess));
                LastHelpString = msg;
                Cleanup();
                IsToollRenderingSomething = false;
                IsExporting = false;
                State = States.WaitingForExport;
                return;
            }
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

        // Don't set playback time yet - wait for the first frame to be rendered at the correct resolution.
        // This ensures triggers fire on the correctly-sized first frame.
        // Playback time will be set in Update() when resolution matches.
        _activeSession.WaitingForFirstFrame = true;
        IsExporting = true;
        LastHelpString = "Rendering...";
    }

    private static int GetRealFrame() => _activeSession!.FrameIndex - MfVideoWriter.SkipImages;
    
    private static string GetTargetFilePath(RenderSettings.RenderModes renderMode) => RenderPaths.GetTargetFilePath(renderMode);

    public static void Cancel(string? reason = null)
    {
        if (_activeSession == null) return;
        var duration = Playback.RunTimeInSecs - _activeSession.ExportStartedTime;
        LastHelpString = reason ?? $"Render cancelled after {StringUtils.HumanReadableDurationFromSeconds(duration)}";
        Cleanup();
        IsToollRenderingSomething = false;
    }

    private static void Cleanup()
    {
        IsExporting = false;

        if (_activeSession != null)
        {
            if (_activeSession.Settings.RenderMode == RenderSettings.RenderModes.Video)
            {
                _activeSession.VideoWriter?.Dispose();
                _activeSession.VideoWriter = null;
            }

            // Audio restoration is now handled automatically by AudioRendering.EndRecording()
            // which is called during the rendering process

            // Release playback time before nulling _activeSession
            RenderTiming.ReleasePlaybackTime(ref _activeSession.Settings, ref _activeSession.Runtime);
            _activeSession = null;
        }
    }
    

    private static bool SaveVideoFrameAndAdvance(ref byte[] audioFrame, int channels, int sampleRate)
    {
        if (Playback.OpNotReady)
        {
            Log.Debug("Waiting for operators to complete");
            return true;
        }

        try
        {
            if (UserSettings.Config.ShowRenderProfilingLogs)
            {
                Log.Debug($"SaveVideoFrameAndAdvance: frame={_frameIndex}, MainOutputTexture null? {MainOutputTexture == null}, audioFrame.Length={audioFrame?.Length}, channels={channels}, sampleRate={sampleRate}");
            }
            if (MainOutputTexture == null)
            {
                Log.Error($"MainOutputTexture is null at frame {_frameIndex}", typeof(RenderProcess));
                LastHelpString = $"MainOutputTexture is null at frame {_frameIndex}";
                Cleanup();
                IsExporting = false;
                IsToollRenderingSomething = false;
                State = States.WaitingForExport;
                return false;
            }
            // Use only the session's VideoWriter
            _activeSession?.VideoWriter?.ProcessFrames(MainOutputTexture, ref audioFrame, channels, sampleRate);
            _frameIndex++;
            RenderTiming.SetPlaybackTimeForFrame(ref _renderSettings, _frameIndex, _frameCount, ref _runtime);
            // Explicitly check for resolution mismatch BEFORE calling video writer
            // This prevents passing bad frames to the writer and allows us to handle the "wait" logic here
            var texture = MainOutputTexture;
            if (texture == null)
            {
                Log.Warning("[SaveVideoFrameAndAdvance] Main output texture is null during export");
                IsExporting = false;
                IsToollRenderingSomething = false;
                State = States.WaitingForExport;
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
            //_activeSession.VideoWriter?.ProcessFrames( MainOutputTexture, ref audioFrame, channels, sampleRate); // Already done above
            
            _activeSession.FrameIndex++;
            RenderTiming.SetPlaybackTimeForFrame(ref _renderSettings, _frameIndex, _frameCount, ref _runtime);
            
            return true;
        }
        catch (Exception e)
        {
            // Check for file-in-use HRESULT (0x80070020)
            if (e is SharpDX.SharpDXException dxEx && (uint)dxEx.HResult == 0x80070020)
            {
                var msg = $"The output file is in use by another process. Please close any application using it and try again.";
                Log.Error(msg, typeof(RenderProcess));
                LastHelpString = msg;
                Cleanup();
                IsExporting = false;
                IsToollRenderingSomething = false;
                State = States.WaitingForExport;
                return false;
            }
            var msg2 = $"Exception in SaveVideoFrameAndAdvance at frame {_frameIndex}: {e.Message}\n{e.StackTrace}";
            Log.Error(msg2, typeof(RenderProcess));
            LastHelpString = msg2;
            Cleanup();
            IsExporting = false;
            IsToollRenderingSomething = false;
            State = States.WaitingForExport;
            return false;
        }
    }

    private static string GetSequenceFilePath()
    {
        var prefix = RenderPaths.SanitizeFilename(UserSettings.Config.RenderSequencePrefix);
        return Path.Combine(_activeSession!.TargetFolder, $"{prefix}_{_activeSession.FrameIndex:0000}.{_activeSession.Settings.FileFormat.ToString().ToLower()}");
    }

    private static bool SaveImageFrameAndAdvance()
    {
        if (MainOutputTexture == null)
        {
            IsExporting = false;
            IsToollRenderingSomething = false;
            State = States.WaitingForExport;
            return false;
        }
        
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
            IsToollRenderingSomething = false;
            State = States.WaitingForExport;
            return false;
        }
    }

    private sealed class ExportSession
    {
        public Mp4VideoWriter? VideoWriter;
        public string TargetFolder = string.Empty;
        public double ExportStartedTime;
        public int FrameIndex;
        public int FrameCount;
        public RenderSettings Settings = null!;
        public RenderTiming.Runtime Runtime;
        public int ResolutionMismatchCount;
        public readonly double ExportStartTimeLocal = 0;
        
        /// <summary>
        /// When true, we're waiting for the first frame to be rendered at the correct resolution
        /// before setting playback time.
        /// </summary>
        public bool WaitingForFirstFrame;
        /// <summary>
        /// Counter for how many frames we've been waiting for first frame resolution match.
        /// </summary>
        public int WaitingForFirstFrameCount;
    }

    public static double Progress => _activeSession == null || _activeSession.FrameCount <= 1 ? 0.0 : (_activeSession.FrameIndex / (double)(_activeSession.FrameCount - 1));

    public static double ExportStartedTimeLocal => _activeSession?.ExportStartTimeLocal ?? 0;

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
        Log.Debug("Screenshot saved in: " + folder);
    }
}
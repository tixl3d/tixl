#nullable enable
using System.Diagnostics;
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

internal static class RenderProcess
{
    public static OutputWindow? OutputWindow;
    public static Type? MainOutputType { get; private set; }
    public static Texture2D? MainOutputTexture;

    public static bool IsExporting => State == States.Exporting;
    public static States State;

    public enum States
    {
        Undefined,
        NoOutputWindow,
        NoValidOutputType,
        NoValidOutputTexture,
        ReadyForExport,
        Exporting,
    }

    public static string LastHelpString { get; private set; } = string.Empty;
    public static string LastTargetDirectory { get; private set; } = string.Empty;

    public static double Progress => (_activeExportSession == null || _activeExportSession.FrameCount <= 1)
                                         ? 0.0
                                         : (_activeExportSession.FrameIndex / (double)(_activeExportSession.FrameCount - 1));

    public static double ExportStartedTimeLocal => _activeExportSession?.ExportStartTimeLocal ?? 0;

    #region main API methods
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

    public static bool TryStartVideoExport()
    {
        if (MainOutputTexture == null || MainOutputTexture.IsDisposed)
            return false;

        var settings = RenderSettings.ForNextExport.Clone();

        if (State != States.ReadyForExport)
        {
            Log.Warning("Export not available");
            return false;
        }

        var targetFilePath = GetTargetFilePath(settings.RenderMode);

        var directory = string.Empty;
        try
        {
            directory = Path.GetDirectoryName(targetFilePath) ?? string.Empty;
        }
        catch (Exception e)
        {
            Log.Warning($"Can't get directory for path: {targetFilePath}:" + e.Message);
        }

        if (!RenderPaths.ValidateOrCreateTargetFolder(targetFilePath))
            return false;

        TryGetRenderResolution(settings, out var requestedResolution);

        var newSession = new ExportSession
                             {
                                 Settings = settings,
                                 FrameCount = RenderTiming.ComputeFrameCount(settings),
                                 ExportStartedTime = Playback.RunTimeInSecs,
                                 FrameIndex = 0,
                                 RenderToFileResolution = requestedResolution,
                                 TargetFilePath = targetFilePath,
                                 TargetDirectory = directory,
                             };

        switch (newSession.Settings.RenderMode)
        {
            case RenderSettings.RenderModes.Video:
                if (File.Exists(targetFilePath))
                {
                    try
                    {
                        File.Delete(targetFilePath);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Can't write {targetFilePath}:" + e.Message);
                        return false;
                    }
                }

                Log.Gated.VideoRender($"""
                                       Initializing Mp4VideoWriter with: path={targetFilePath}
                                       renderedSize={newSession.RenderToFileResolution.Width}x{newSession.RenderToFileResolution.Height}
                                       bitrate={settings.Bitrate}
                                       framerate={settings.FrameRate}
                                       audio={settings.ExportAudio}
                                       channels={RenderAudioInfo.SoundtrackChannels()}
                                       sampleRate={RenderAudioInfo.SoundtrackSampleRate()}
                                       """);
                break;

            case RenderSettings.RenderModes.ImageSequence:
            default:

                ScreenshotWriter.ClearQueue();
                break;
        }

        LastTargetDirectory = Path.GetDirectoryName(targetFilePath) ?? string.Empty;

        State = States.Exporting;

        LastHelpString = "Rendering...";
        _activeExportSession = newSession;
        return true;
    }

    public static void Cancel(string? reason = null)
    {
        if (_activeExportSession == null)
        {
            State = States.Undefined;
            return;
        }

        var duration = Playback.RunTimeInSecs - _activeExportSession.ExportStartedTime;
        LastHelpString = reason ?? $"Render cancelled after {StringUtils.HumanReadableDurationFromSeconds(duration)}";
        CleanupSession();
    }
    #endregion

    private static bool TryInitVideoWriterWithFinalResolution(ExportSession session)
    {
        try
        {
            session.VideoWriter = new Mp4VideoWriter(session);

            Log.Gated.VideoRender($"Mp4VideoWriter initialized: " +
                                  $"Codec=H.264" +
                                  $"FileFormat=mp4" +
                                  $"Bitrate={session.Settings.Bitrate}" +
                                  $"Framerate={session.Settings.FrameRate}" +
                                  $"Channels={RenderAudioInfo.SoundtrackChannels()}" +
                                  $"SampleRate={RenderAudioInfo.SoundtrackSampleRate()}");
        }
        catch (Exception ex)
        {
            var msg = $"Failed to initialize Mp4VideoWriter: {ex.Message}\n{ex.StackTrace}";
            Log.Error(msg);
            LastHelpString = msg;
            CleanupSession();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Needs to be called once per frame
    /// </summary>
    public static void Update()
    {
        if (!OutputWindow.TryGetPrimaryOutputWindow(out OutputWindow))
        {
            State = States.NoOutputWindow;
            return;
        }

        MainOutputTexture = OutputWindow.GetCurrentTexture();
        if (MainOutputTexture == null || MainOutputTexture.IsDisposed)
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

        if (State != States.Exporting)
        {
            State = States.ReadyForExport;
        }

        HandleRenderShortCuts();

        if (!IsExporting)
            return;

        if (_activeExportSession == null)
        {
            Log.Warning("Reverting inconsistent render export state");
            State = States.Undefined;
            return;
        }

        ExportOutputTexture(MainOutputTexture, _activeExportSession);
    }

    private static void ExportOutputTexture(Texture2D mainOutputTexture, ExportSession session)
    {
        var settings = session.Settings;
        var savingSuccessful = false;

        // Ensure resolution is valid
        var currentResolution = new Int2(mainOutputTexture.Description.Width, mainOutputTexture.Description.Height);
        var resolutionMatches = currentResolution == session.RenderToFileResolution;

        if (!resolutionMatches)
        {
            if (++session.ResolutionMismatchCount < 2)
            {
                Log.Debug($"Waiting for resolution {session.RenderToFileResolution.ToResolutionString()}...");
                return;
            }

            Log.Debug("Falling back to " + currentResolution.ToResolutionString());
            session.RenderToFileResolution = currentResolution;
        }

        if (session.VideoWriter == null)
        {
            if (!TryInitVideoWriterWithFinalResolution(session))
                return;
        }

        switch (settings.RenderMode)
        {
            case RenderSettings.RenderModes.Video:
            {
                var audioFrame = ComputeAudioBufferForVideoFrame(session);
                savingSuccessful = SaveVideoFrameAndAdvance(session, mainOutputTexture, ref audioFrame, RenderAudioInfo.SoundtrackChannels(),
                                                            RenderAudioInfo.SoundtrackSampleRate());
                break;
            }
            case RenderSettings.RenderModes.ImageSequence:
                AudioRendering.GetLastMixDownBuffer(Playback.LastFrameDuration);

                // For image export, also update metering for UI/graph
                // Use FxTimeInBars as a substitute for LocalFxTime
                AudioRendering.EvaluateAllAudioMeteringOutputs(Playback.Current.FxTimeInBars);
                savingSuccessful = TrySaveImageFrameAndAdvance(mainOutputTexture);
                break;
        }

        // Update stats
        var effectiveFrameCount = settings.RenderMode == RenderSettings.RenderModes.Video ? session.FrameCount : session.FrameCount + 2;

        var exportedFrameIndex = session.FrameIndex - MfVideoWriter.SkipImages;

        var currentFrame = settings.RenderMode == RenderSettings.RenderModes.Video
                               ? exportedFrameIndex
                               : session.FrameIndex + 1;

        var completed = currentFrame >= effectiveFrameCount || !savingSuccessful;
        if (!completed)
            return;

        var duration = Playback.RunTimeInSecs - session.ExportStartedTime;
        var successful = savingSuccessful ? "successfully" : "unsuccessfully";
        LastHelpString = $"Render {GetTargetFilePath(settings.RenderMode)} finished {successful} in {StringUtils.HumanReadableDurationFromSeconds(duration)}";
        Log.Debug(LastHelpString);

        if (savingSuccessful)
        {
            if (settings.RenderMode == RenderSettings.RenderModes.Video && settings.AutoIncrementVersionNumber)
            {
                RenderPaths.TryIncrementVideoFileNameInUserSettings();
            }
            else if (settings.RenderMode == RenderSettings.RenderModes.ImageSequence && settings.AutoIncrementSubFolder)
            {
                if (settings.CreateSubFolder)
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

        CleanupSession();
    }

    /// <summary>
    /// Computes the final render to file resolution from the current texture size and resolution scale factor. 
    /// </summary>
    /// <remarks>
    /// Eventually this should also include rounding to even file sizes or clamping to the codec sizes provided in the setting. 
    /// </remarks>
    public static bool TryGetRenderResolution(RenderSettings settings, out Int2 resolution)
    {
        resolution = Int2.Zero;
        if (State != States.ReadyForExport && State != States.Exporting)
            return false;

        if (MainOutputTexture == null || MainOutputTexture.IsDisposed)
            return false;

        // TODO: clamp for valid encoding resolutions...
        resolution = new Int2(
                              ((int)(MainOutputTexture.Description.Width * settings.ResolutionFactor)).Clamp(1, 16384),
                              ((int)(MainOutputTexture.Description.Height * settings.ResolutionFactor)).Clamp(1, 16384));
        return true;
    }

    public static RenderSettings GetActiveOrRequestedSettings()
    {
        return State == States.Exporting && _activeExportSession != null
                   ? _activeExportSession.Settings
                   : RenderSettings.ForNextExport;
    }

    public static bool TryGetActiveExportResolution(out Int2 resolution)
    {
        resolution = Int2.One;
        if (State != States.Exporting || _activeExportSession == null)
            return false;

        resolution = _activeExportSession.RenderToFileResolution;
        return true;
    }

    private static byte[] ComputeAudioBufferForVideoFrame(ExportSession session)
    {
        // Use the new full mixdown buffer for audio export
        double localFxTime = session.FrameIndex / session.Settings.FrameRate;
        Log.Gated.VideoRender($"Requested recording from {0.0000:F4} to {(session.FrameCount / session.Settings.FrameRate):F4} seconds");
        Log.Gated.VideoRender($"Actually recording from {(session.FrameIndex / session.Settings.FrameRate):F4} to {((session.FrameIndex + 1) / session.Settings.FrameRate):F4} seconds due to frame raster");
        var audioFrameFloat = AudioRendering.GetFullMixDownBuffer(1.0 / session.Settings.FrameRate);

        // Safety: ensure audioFrameFloat is valid and sized
        if (audioFrameFloat == null || audioFrameFloat.Length == 0)
        {
            Log.Error($"RenderProcess: AudioRendering.GetFullMixDownBuffer returned null or empty at frame {session.FrameIndex}");
            var sampleRate = RenderAudioInfo.SoundtrackSampleRate();
            var channels = RenderAudioInfo.SoundtrackChannels();
            var floatCount = (int)Math.Max(Math.Round((1.0 / session.Settings.FrameRate) * sampleRate), 0.0) * channels;
            audioFrameFloat = new float[floatCount]; // silence
        }

        // Convert float[] to byte[] for the writer
        var audioFrame = new byte[audioFrameFloat.Length * sizeof(float)];
        Buffer.BlockCopy(audioFrameFloat, 0, audioFrame, 0, audioFrame.Length);

        // Force metering outputs to update for UI/graph
        AudioRendering.EvaluateAllAudioMeteringOutputs(localFxTime, audioFrameFloat);
        return audioFrame;
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
                TryStartVideoExport();
            }
        }

        if (UserActions.RenderScreenshot.Triggered())
        {
            TryRenderScreenShot();
        }
    }

    private static string GetTargetFilePath(RenderSettings.RenderModes renderMode) => RenderPaths.GetTargetFilePath(renderMode);

    private static void CleanupSession()
    {
        if (_activeExportSession == null)
            return;

        if (_activeExportSession.Settings.RenderMode == RenderSettings.RenderModes.Video)
        {
            try
            {
                _activeExportSession.VideoWriter?.Dispose();
                _activeExportSession.VideoWriter = null;
            }
            catch (Exception e)
            {
                Log.Debug("Failed to cleanup video writer: " + e.Message);
            }
        }

        // Audio restoration is now handled automatically by AudioRendering.EndRecording()
        // which is called during the rendering process

        // Release playback time before nulling _activeSession
        RenderTiming.ReleasePlaybackTime(ref _activeExportSession.Settings, ref _activeExportSession.Runtime);
        _activeExportSession = null;

        State = States.ReadyForExport;
    }

    private static bool SaveVideoFrameAndAdvance(ExportSession session, Texture2D outputTexture, ref byte[] audioFrame, int channels, int sampleRate)
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
                Log.Debug($"""
                           SaveVideoFrameAndAdvance: frame={session.FrameIndex}
                           MainOutputTexture null? {MainOutputTexture == null}
                           audioFrame.Length={audioFrame?.Length}
                           channels={channels}
                           sampleRate={sampleRate}
                           """);
            }

            RenderTiming.SetPlaybackTimeForFrame(session);

            // We need to wait for one frame of the outputTexture to update to the new time 
            if (session.FrameIndex > 0)
                session.VideoWriter?.ProcessFrames(outputTexture, ref audioFrame, channels, sampleRate);

            session.FrameIndex++;
            return true;
        }
        catch (Exception e)
        {
            var wasFileWriteException = e is SharpDX.SharpDXException dxEx && (uint)dxEx.HResult == 0x80070020;
            var msg = wasFileWriteException
                          ? "The output file is in use by another process. Please close any application using it and try again."
                          : $"Exception in SaveVideoFrameAndAdvance at frame {session.FrameIndex}: {e.Message}\n{e.StackTrace}";

            Log.Error(msg);
            LastHelpString = msg;
            CleanupSession();
        }

        return false;
    }

    private static string GetSequenceFilePath()
    {
        var prefix = RenderPaths.SanitizeFilename(UserSettings.Config.RenderSequencePrefix);
        return Path.Combine(_activeExportSession!.TargetDirectory,
                            $"{prefix}_{_activeExportSession.FrameIndex:0000}.{_activeExportSession.Settings.FileFormat.ToString().ToLower()}");
    }

    private static bool TrySaveImageFrameAndAdvance(Texture2D mainOutputTexture)
    {
        try
        {
            if (!ScreenshotWriter.StartSavingToFile(mainOutputTexture, GetSequenceFilePath(), _activeExportSession!.Settings.FileFormat))
                return false;

            _activeExportSession.FrameIndex++;
            RenderTiming.SetPlaybackTimeForFrame(_activeExportSession);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning(e.Message);
            return false;
        }
    }

    private static string ToResolutionString(this Int2 resolution)
    {
        return $"{resolution.Width}×{resolution.Height}";
    }

    internal sealed class ExportSession
    {
        public Mp4VideoWriter? VideoWriter;
        public string TargetDirectory = string.Empty;
        public string TargetFilePath = string.Empty;
        public double ExportStartedTime;
        public int FrameIndex;
        public int FrameCount;
        public RenderSettings Settings = null!;
        public RenderTiming.Runtime Runtime;
        public readonly double ExportStartTimeLocal = 0;
        public Int2 RenderToFileResolution;

        public int ResolutionMismatchCount;
    }

    private static ExportSession? _activeExportSession;
}
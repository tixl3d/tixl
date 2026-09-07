#nullable enable
using System.IO;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Core.Video;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.UiModel;
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

    /// <summary>Export progress in 0..1, or a negative value when the active export is open-ended
    /// (<see cref="RenderSettings.TimeRanges.Continuous"/>) and has no determinate end — the footer shows an
    /// activity indicator in that case.</summary>
    public static double Progress => IsContinuousActive
                                         ? -1.0
                                         : (_activeExportSession == null || _activeExportSession.FrameCount <= 1)
                                             ? 0.0
                                             : (_activeExportSession.FrameIndex / (double)(_activeExportSession.FrameCount - 1));

    /// <summary>True while an open-ended continuous capture is running.</summary>
    public static bool IsContinuousActive
        => _activeExportSession is { Settings.TimeRange: RenderSettings.TimeRanges.Continuous };

    /// <summary>Frames written so far by the active export (for the continuous-capture activity readout).</summary>
    public static int CapturedFrameCount => _activeExportSession?.FrameIndex ?? 0;

    /// <summary>Seconds since the active export began (0 when none is running).</summary>
    public static double ExportElapsedSeconds
        => _activeExportSession == null ? 0.0 : Playback.RunTimeInSecs - _activeExportSession.ExportStartedTime;

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

        var settings = RenderSettings.Current.Clone();

        // Realtime continuous capture grabs the live output and doesn't take over playback, so there is no
        // synced audio path yet — keep the writer audio-free regardless of the (greyed) checkbox.
        if (settings.TimeRange == RenderSettings.TimeRanges.Continuous
            && settings.ContinuousClock == RenderSettings.ContinuousCaptureClock.Realtime)
        {
            settings.ExportAudio = false;
            settings.ResolutionFactor = 1f; // realtime grabs the live texture as-is — capture at native size
        }

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
                                       Initializing video export with: path={targetFilePath}
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

    /// <summary>Finalizes an open-ended continuous capture as a success — the file is muxed and kept, and the
    /// version auto-increments like a normal render. (Unlike <see cref="Cancel"/>, which reads as discarded.)</summary>
    public static void StopContinuous()
    {
        if (_activeExportSession == null)
        {
            State = States.Undefined;
            return;
        }

        var session = _activeExportSession;
        var settings = session.Settings;
        var duration = Playback.RunTimeInSecs - session.ExportStartedTime;
        LastHelpString = $"Captured {session.FrameIndex} frames ({StringUtils.HumanReadableDurationFromSeconds(duration)}) "
                         + $"to {GetTargetFilePath(settings.RenderMode)}";
        Log.Debug(LastHelpString);

        if (settings.RenderMode == RenderSettings.RenderModes.Video && settings.AutoIncrementVersionNumber)
        {
            RenderPaths.TryIncrementVideoFileName();
            ProjectView.Focused?.CompositionInstance?.Symbol.GetSymbolUi()?.FlagAsModified();
        }

        CleanupSession();
    }
    #endregion

    private static bool TryInitVideoWriterWithFinalResolution(ExportSession session)
    {
        try
        {
            var codec = session.Settings.VideoCodec;
            if (VideoEncoderAvailabilityCache.GetBlocking(codec).Kind == VideoEncoderKind.Unavailable)
            {
                LastHelpString = $"Can't render {codec}: this FFmpeg build has no encoder for it.";
                Log.Warning(LastHelpString);
                CleanupSession();
                return false;
            }

            if (FfmpegVideoExportWriter.TryCreate(session, out var ffmpegError) is not { } ffmpegWriter)
            {
                LastHelpString = "Can't render: the FFmpeg video encoder is unavailable. " + ffmpegError;
                Log.Error(LastHelpString);
                CleanupSession();
                return false;
            }

            session.VideoWriter = ffmpegWriter;
            Log.Debug("Render-export: using the FFmpeg encoder.");

            Log.Gated.VideoRender($"FFmpeg video writer initialized: " +
                                  $"Codec={session.Settings.VideoCodec} " +
                                  $"Bitrate={session.Settings.Bitrate} " +
                                  $"Framerate={session.Settings.FrameRate} " +
                                  $"Channels={RenderAudioInfo.SoundtrackChannels()} " +
                                  $"SampleRate={RenderAudioInfo.SoundtrackSampleRate()}");
        }
        catch (Exception ex)
        {
            var msg = $"Failed to initialize the FFmpeg video writer: {ex.Message}\n{ex.StackTrace}";
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
        if (OutputWindow.TryGetPrimaryOutputWindow(out OutputWindow))
        {
            MainOutputTexture = OutputWindow.GetCurrentTexture();
            MainOutputType = OutputWindow.ShownInstance?.Outputs.FirstOrDefault()?.ValueType;
        }
        else if (LayoutHandling.FocusMode && ProjectView.Focused?.GraphImageBackground is { IsActive: true } background)
        {
            // In focus mode the output window is hidden and the output renders into the graph background,
            // so source the texture from there to keep render shortcuts working.
            OutputWindow = null;
            MainOutputTexture = background.GetCurrentTexture();
            MainOutputType = background.OutputInstance?.Outputs.FirstOrDefault()?.ValueType;
        }
        else
        {
            State = States.NoOutputWindow;
            return;
        }

        if (MainOutputTexture == null || MainOutputTexture.IsDisposed)
        {
            State = States.NoValidOutputTexture;
            return;
        }

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

        // Image sequences are written by the ScreenshotWriter — creating an FFmpeg writer would fail here
        // because the sequence target path is a filename stem without a container extension.
        if (settings.RenderMode == RenderSettings.RenderModes.Video && session.VideoWriter == null)
        {
            if (!TryInitVideoWriterWithFinalResolution(session))
                return;
        }

        var isRealtimeContinuous = settings.TimeRange == RenderSettings.TimeRanges.Continuous
                                   && settings.ContinuousClock == RenderSettings.ContinuousCaptureClock.Realtime;

        if (isRealtimeContinuous)
        {
            // Grab the live output without taking over playback, paced to the target FPS.
            savingSuccessful = WriteRealtimeContinuousFrames(session, mainOutputTexture);
        }
        else
        {
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
                    // Drive registered audio ops (buses) before pulling the mix so routing/params apply
                    // to this frame's audio; the mix then feeds the analysis that drives animations.
                    double localFxTime = session.FrameIndex / session.Settings.FrameRate;
                    AudioRendering.EvaluateAllAudioMeteringOutputs(localFxTime);
                    AudioRendering.GetFullMixDownBuffer(1.0 / session.Settings.FrameRate);
                    savingSuccessful = TrySaveImageFrameAndAdvance(mainOutputTexture);
                    break;
            }
        }

        // Open-ended capture has no frame-count target: it ends only on an error or the user's stop press.
        if (settings.TimeRange == RenderSettings.TimeRanges.Continuous)
        {
            if (savingSuccessful)
                return;

            LastHelpString = "Continuous capture stopped after an error.";
            Log.Warning(LastHelpString);
            CleanupSession();
            return;
        }

        // Update stats
        var effectiveFrameCount = settings.RenderMode == RenderSettings.RenderModes.Video ? session.FrameCount : session.FrameCount + 2;

        var exportedFrameIndex = session.FrameIndex - WarmupFramesToSkip;

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
            var incremented = false;
            if (settings.RenderMode == RenderSettings.RenderModes.Video && settings.AutoIncrementVersionNumber)
            {
                RenderPaths.TryIncrementVideoFileName();
                incremented = true;
            }
            else if (settings.RenderMode == RenderSettings.RenderModes.ImageSequence && settings.AutoIncrementSubFolder)
            {
                if (settings.CreateSubFolder)
                {
                    RenderSettings.Current.SequenceFileName = RenderPaths.GetNextIncrementedPath(RenderSettings.Current.SequenceFileName);
                }
                else
                {
                    RenderSettings.Current.SequencePrefix = RenderPaths.GetNextIncrementedPath(RenderSettings.Current.SequencePrefix);
                }
                incremented = true;
            }

            if (incremented)
            {
                ProjectView.Focused?.CompositionInstance?.Symbol.GetSymbolUi()?.FlagAsModified();
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
                   : RenderSettings.Current;
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

        // Drive registered audio ops (buses) BEFORE pulling the mix, so their routing, animated params
        // and trigger edges apply to the audio of this frame — not the next one.
        AudioRendering.EvaluateAllAudioMeteringOutputs(localFxTime);

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
                // For continuous capture the second press is a normal stop (keep the file), not a cancel.
                if (IsContinuousActive)
                    StopContinuous();
                else
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

        // Realtime continuous capture never takes over playback, so there is nothing to release — doing so
        // would force PlaybackSpeed to 0 and yank the user's live playback when they stop the capture.
        var wasRealtimeContinuous = _activeExportSession.Settings.TimeRange == RenderSettings.TimeRanges.Continuous
                                    && _activeExportSession.Settings.ContinuousClock == RenderSettings.ContinuousCaptureClock.Realtime;
        if (!wasRealtimeContinuous)
        {
            // Release playback time before nulling _activeSession
            RenderTiming.ReleasePlaybackTime(ref _activeExportSession.Settings, ref _activeExportSession.Runtime);
        }

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
                           audioFrame.Length={audioFrame.Length}
                           channels={channels}
                           sampleRate={sampleRate}
                           """);
            }

            RenderTiming.SetPlaybackTimeForFrame(session);

            // Skip the warmup frame(s): the output texture needs one frame to settle on the new playback time.
            if (session.FrameIndex >= WarmupFramesToSkip)
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

    // Realtime continuous capture: write as many frames of the current live texture as the wall clock says are
    // due, so the file stays at a constant target FPS even though the editor refresh rate varies. Playback is
    // left untouched (the user controls it live).
    private static bool WriteRealtimeContinuousFrames(ExportSession session, Texture2D texture)
    {
        var fps = session.Settings.FrameRate;
        if (fps <= 0)
            return false;

        if (!session.RealtimeClockAnchored)
        {
            // Start the wall clock from the first frame the (lazily created) writer is ready for, so the
            // init delay doesn't count as captured time.
            session.ExportStartedTime = Playback.RunTimeInSecs;
            session.RealtimeClockAnchored = true;
            return true;
        }

        var elapsed = Playback.RunTimeInSecs - session.ExportStartedTime;
        var framesDue = (int)(elapsed * fps);
        var framesToWrite = framesDue - session.FrameIndex;
        if (framesToWrite <= 0)
            return true; // editor running faster than the target FPS — skip a grab this frame

        try
        {
            // A long stall (modal open, editor paused) shouldn't flood the file with duplicates: write one
            // frame and resync the wall-clock baseline rather than filling the whole gap.
            const int maxCatchUpFrames = 4;
            if (framesToWrite > maxCatchUpFrames)
            {
                WriteOneRealtimeFrame(session, texture);
                session.ExportStartedTime = Playback.RunTimeInSecs - session.FrameIndex / fps;
                return true;
            }

            for (var i = 0; i < framesToWrite; i++)
                WriteOneRealtimeFrame(session, texture);

            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Continuous capture failed at frame {session.FrameIndex}: {e.Message}");
            return false;
        }
    }

    private static void WriteOneRealtimeFrame(ExportSession session, Texture2D texture)
    {
        if (session.Settings.RenderMode == RenderSettings.RenderModes.Video)
        {
            var noAudio = Array.Empty<byte>();
            session.VideoWriter?.ProcessFrames(texture, ref noAudio,
                                               RenderAudioInfo.SoundtrackChannels(), RenderAudioInfo.SoundtrackSampleRate());
        }
        else
        {
            ScreenshotWriter.StartSavingToFile(texture, GetSequenceFilePath(session), session.Settings.FileFormat);
        }

        session.FrameIndex++;
    }

    /// <summary>
    /// The frame file for the session's current frame index. The prefix comes from the session's target path so
    /// an auto-incremented version is honored and later edits to the settings can't rename mid-render.
    /// </summary>
    private static string GetSequenceFilePath(ExportSession session)
    {
        var prefix = RenderPaths.SanitizeFilename(Path.GetFileName(session.TargetFilePath));
        return Path.Combine(session.TargetDirectory,
                            $"{prefix}_{session.FrameIndex:0000}.{session.Settings.FileFormat.ToString().ToLower()}");
    }

    private static bool TrySaveImageFrameAndAdvance(Texture2D mainOutputTexture)
    {
        try
        {
            if (!ScreenshotWriter.StartSavingToFile(mainOutputTexture, GetSequenceFilePath(_activeExportSession!), _activeExportSession!.Settings.FileFormat))
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
        public IRenderVideoWriter? VideoWriter;
        public string TargetDirectory = string.Empty;
        public string TargetFilePath = string.Empty;
        public double ExportStartedTime;
        public int FrameIndex;
        public int FrameCount;
        public RenderSettings Settings = null!;
        public RenderTiming.Runtime Runtime;
        public Int2 RenderToFileResolution;

        public int ResolutionMismatchCount;

        // Realtime continuous capture only: set once the writer is ready and the wall clock is anchored.
        public bool RealtimeClockAnchored;
    }

    private static ExportSession? _activeExportSession;

    // The output texture needs one frame to settle on each new playback time, so the first rendered frame is a
    // warmup that isn't written. The frame-count math accounts for it.
    private const int WarmupFramesToSkip = 1;
}
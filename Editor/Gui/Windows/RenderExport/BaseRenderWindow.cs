#nullable enable
using System.IO;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

internal abstract class BaseRenderWindow : Window
{

    protected static int SoundtrackChannels()
    {
        var composition = ProjectView.Focused?.CompositionInstance;
        if (composition == null)
            return AudioEngine.GetClipSampleRate(null);
        
        PlaybackUtils.FindPlaybackSettingsForInstance(composition, out var instanceWithSettings, out var settings);
        if (settings.GetMainSoundtrack(instanceWithSettings, out var soundtrack))
            return AudioEngine.GetClipChannelCount(soundtrack);
        
        return AudioEngine.GetClipChannelCount(null);
    }

    protected static int SoundtrackSampleRate()
    {
        var composition = ProjectView.Focused?.CompositionInstance;

        if (composition == null)
            return AudioEngine.GetClipSampleRate(null);
        
        PlaybackUtils.FindPlaybackSettingsForInstance(composition, out var instanceWithSettings, out var settings);
        return AudioEngine.GetClipSampleRate(settings.GetMainSoundtrack(instanceWithSettings, out var soundtrack) 
                                                 ? soundtrack 
                                                 : null);
    }

    protected static void SetRenderingStarted()
    {
        IsToollRenderingSomething = true;
    }
    
    protected static void RenderingFinished()
    {
        IsToollRenderingSomething = false;
    }

    public static bool IsToollRenderingSomething { get; private set; }

    protected static void DrawTimeSetup()
    {
        FormInputs.SetIndentToParameters();
        FormInputs.AddSegmentedButtonWithLabel(ref _timeRange, "Render Range");
        ApplyTimeRange();
       
        FormInputs.AddVerticalSpace();
        
        // Convert times if reference time selection changed
        var oldTimeReference = _timeReference;

        if (FormInputs.AddSegmentedButtonWithLabel(ref _timeReference, "Defined as"))
        {
            _startTimeInBars = (float)ConvertReferenceTime(_startTimeInBars, oldTimeReference, _timeReference);
            _endTimeInBars = (float)ConvertReferenceTime(_endTimeInBars, oldTimeReference, _timeReference);
        }

        var changed = false;
        changed |= FormInputs.AddFloat($"Start in {_timeReference}", ref _startTimeInBars);
        changed |= FormInputs.AddFloat($"End in {_timeReference}", ref _endTimeInBars);
        if (changed)
        {
            _timeRange = TimeRanges.Custom;
        }
        
        FormInputs.AddVerticalSpace();
        
        // Change FPS if required
        FormInputs.AddFloat("FPS", ref Fps, 0);
        if (Fps < 0) Fps = -Fps;
        if (Fps != 0)
        {
            _startTimeInBars = (float)ConvertFps(_startTimeInBars, _lastValidFps, Fps);
            _endTimeInBars = (float)ConvertFps(_endTimeInBars, _lastValidFps, Fps);
            _lastValidFps = Fps;
        }

        var startTimeInSeconds = ReferenceTimeToSeconds(_startTimeInBars, _timeReference);
        var endTimeInSeconds = ReferenceTimeToSeconds(_endTimeInBars, _timeReference);
        FrameCount = (int)Math.Round((endTimeInSeconds - startTimeInSeconds) * Fps);
        
        FormInputs.AddFloat($"Resolution Factor", ref _resolutionFactor, 0.125f, 4, 0.1f, true,
                            "A factor applied to the output resolution of the rendered frames.");
        
        if (FormInputs.AddInt($"Motion Blur Samples", ref _overrideMotionBlurSamples, -1, 50, 1,
                              "This requires a [RenderWithMotionBlur] operator. Please check its documentation."))
        {
            _overrideMotionBlurSamples = _overrideMotionBlurSamples.Clamp(-1, 50);
        }
    }

    protected static bool ValidateOrCreateTargetFolder(string targetFile)
    {
        var directory = Path.GetDirectoryName(targetFile);
        if (targetFile != directory && File.Exists(targetFile))
        {
            // FIXME: get a nicer popup window here...
            var result = BlockingWindow.Instance.ShowMessageBox("File exists. Overwrite?", "Render Video", "Yes", "No");
            return (result == "Yes");
        }

        if (directory == null || Directory.Exists(directory))
            return true;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to create target folder '{directory}': {e.Message}");
            return false;
        }

        return true;
    }
    

    private static void ApplyTimeRange()
    {
        switch (_timeRange)
        {
            case TimeRanges.Custom:
                break;
            case TimeRanges.Loop:
            {
                var playback = Playback.Current; // TODO, this should be non-static eventually
                var startInSeconds = playback.SecondsFromBars(playback.LoopRange.Start);
                var endInSeconds = playback.SecondsFromBars(playback.LoopRange.End);
                _startTimeInBars = (float)SecondsToReferenceTime(startInSeconds, _timeReference);
                _endTimeInBars = (float)SecondsToReferenceTime(endInSeconds, _timeReference);
                break;
            }
            case TimeRanges.Soundtrack:
            {
                if (PlaybackUtils.TryFindingSoundtrack(out var handle, out _))
                {
                    var playback = Playback.Current; // TODO, this should be non-static eventually
                    var soundtrackClip = handle.Clip;
                    _startTimeInBars = (float)SecondsToReferenceTime(playback.SecondsFromBars(soundtrackClip.StartTime), _timeReference);
                    if (soundtrackClip.EndTime > 0)
                    {
                        _endTimeInBars = (float)SecondsToReferenceTime(playback.SecondsFromBars(soundtrackClip.EndTime), _timeReference);
                    }
                    else
                    {
                        _endTimeInBars = (float)SecondsToReferenceTime(soundtrackClip.LengthInSeconds, _timeReference);
                    }
                }
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static double ConvertReferenceTime(double time,
                                               TimeReference oldTimeReference,
                                               TimeReference newTimeReference)
    {
        // Only convert time value if time reference changed
        if (oldTimeReference == newTimeReference) return time;

        var seconds = ReferenceTimeToSeconds(time, oldTimeReference);
        return SecondsToReferenceTime(seconds, newTimeReference);
    }

    private static double ConvertFps(double time, double oldFps, double newFps)
    {
        // Only convert FPS if values are valid
        if (oldFps == 0 || newFps == 0) return time;

        return time / oldFps * newFps;
    }

    private static double ReferenceTimeToSeconds(double time, TimeReference timeReference)
    {
        var playback = Playback.Current; // TODO, this should be non-static eventually
        switch (timeReference)
        {
            case TimeReference.Bars:
                return playback.SecondsFromBars(time);
            case TimeReference.Seconds:
                return time;
            case TimeReference.Frames:
                if (Fps != 0)
                    return time / Fps;
                else
                    return time / 60.0;
        }

        // This is an error, don't change the value
        return time;
    }

    private static double SecondsToReferenceTime(double timeInSeconds, TimeReference timeReference)
    {
        var playback = Playback.Current; // TODO, this should be non-static eventually
        switch (timeReference)
        {
            case TimeReference.Bars:
                return playback.BarsFromSeconds(timeInSeconds);
            case TimeReference.Seconds:
                return timeInSeconds;
            case TimeReference.Frames:
                if (Fps != 0)
                    return timeInSeconds * Fps;
                else
                    return timeInSeconds * 60.0;
        }

        // This is an error, don't change the value
        return timeInSeconds;
    }

    protected static void SetPlaybackTimeForThisFrame()
    {
        
        // get playback settings
        var composition = ProjectView.Focused?.CompositionInstance;
        if (composition == null)
        {
            Log.Warning("Can't find focused composition instance.");
            return;
        }
        
        PlaybackUtils.FindPlaybackSettingsForInstance(composition, out var instanceWithSettings, out var settings);

        // change settings for all playback before calculating times
        Playback.Current.Bpm = settings.Bpm;
        Playback.Current.PlaybackSpeed = 0.0;
        Playback.Current.Settings = settings;
        Playback.Current.FrameSpeedFactor = Fps/60.0;

        // set user time in secs for video playback
        double startTimeInSeconds = ReferenceTimeToSeconds(_startTimeInBars, _timeReference);
        double endTimeInSeconds = startTimeInSeconds + (FrameCount - 1) / Fps;
        var oldTimeInSecs = Playback.Current.TimeInSecs;
        Playback.Current.TimeInSecs = MathUtils.Lerp(startTimeInSeconds, endTimeInSeconds, Progress);
        var adaptedDeltaTime = Math.Max(Playback.Current.TimeInSecs - oldTimeInSecs + _timingOverhang, 0.0);

        // set user time in secs for audio playback
        if (settings.GetMainSoundtrack(instanceWithSettings, out var soundtrack))
            AudioEngine.UseAudioClip(soundtrack, Playback.Current.TimeInSecs);

        if (!_audioRecording)
        {
            _timingOverhang = 0.0;
            adaptedDeltaTime = 1.0 / Fps;

            Playback.Current.IsRenderingToFile = true;
            Playback.Current.PlaybackSpeed = 1.0;

            AudioRendering.PrepareRecording(Playback.Current, Fps);

            double requestedEndTimeInSeconds = ReferenceTimeToSeconds(_endTimeInBars, _timeReference);
            double actualEndTimeInSeconds = startTimeInSeconds + FrameCount / Fps;

            Log.Debug($"Requested recording from {startTimeInSeconds:0.0000} to {requestedEndTimeInSeconds:0.0000} seconds");
            Log.Debug($"Actually recording from {startTimeInSeconds:0.0000} to {actualEndTimeInSeconds:0.0000} seconds due to frame raster");
            Log.Debug($"Using {Playback.Current.Bpm} bpm");

            _audioRecording = true;
        }

        // update audio parameters, respecting looping etc.
        Playback.Current.Update();

        var bufferLengthInMs = (int)Math.Floor(1000.0 * adaptedDeltaTime);
        _timingOverhang = adaptedDeltaTime - bufferLengthInMs / 1000.0;
        _timingOverhang = Math.Max(_timingOverhang, 0.0);

        AudioEngine.CompleteFrame(Playback.Current, bufferLengthInMs / 1000.0);
    }

    protected static void ReleasePlaybackTime()
    {
        AudioRendering.EndRecording(Playback.Current, Fps);

        Playback.Current.TimeInSecs = ReferenceTimeToSeconds(_endTimeInBars, _timeReference);
        Playback.Current.IsRenderingToFile = false;
        Playback.Current.PlaybackSpeed = 0.0;
        Playback.Current.FrameSpeedFactor = 1.0;    // TODO: this should use current display frame rate
        Playback.Current.Update();

        _audioRecording = false;
    }

    internal override List<Window> GetInstances()
    {
        return new List<Window>();
    }

    protected static bool FindIssueWithTexture(Texture2D? texture, List<SharpDX.DXGI.Format> supportedInputFormats, out string warning)
    {
        if (texture == null || texture.IsDisposed)
        {
            warning = "You have selected an operator that does not render. ";
            return true;
        }

        warning = string.Empty;
        return false;
    }

    protected const string PreferredInputFormatHint = "Ready to render.";

    protected static double Progress => (FrameCount <= 1) ? 0 :
        (FrameIndex / (double)(FrameCount - 1)).Clamp(0, 1);

    private static TimeRanges _timeRange = TimeRanges.Custom;
    private static TimeReference _timeReference;
    private static float _startTimeInBars;
    private static float _endTimeInBars = 4.0f; 
    protected static float Fps = 60.0f;
    private static float _resolutionFactor = 1;
    private static float _lastValidFps = Fps;

    private static double _timingOverhang; // Time that could not be updated due to MS resolution (in seconds)
    private static bool _audioRecording; 

    // ReSharper disable once InconsistentNaming
    internal static int OverrideMotionBlurSamples => _overrideMotionBlurSamples;
    private static int _overrideMotionBlurSamples = -1;

    protected static int FrameIndex;
    protected static int FrameCount;
    
    private enum TimeReference
    {
        Bars,
        Seconds,
        Frames
    }

    private enum TimeRanges
    {
        Custom,
        Loop,
        Soundtrack,
    }

}
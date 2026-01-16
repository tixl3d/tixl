#nullable enable
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Timing;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static class RenderTiming
{
    public struct Runtime
    {
        public double TimingOverhang;  // leftover time due to ms granularity
        public bool AudioRecording;
    }

    public static void ApplyTimeRange(RenderSettings.TimeRanges range, RenderSettings renderSettings)
    {
        switch (range)
        {
            case RenderSettings.TimeRanges.Custom:
                break;

            case RenderSettings.TimeRanges.Loop:
            {
                var playback = Playback.Current;
                var startInSeconds = playback.SecondsFromBars(playback.LoopRange.Start);
                var endInSeconds = playback.SecondsFromBars(playback.LoopRange.End);
                renderSettings.StartInBars = (float)SecondsToReferenceTime(startInSeconds, renderSettings.Reference, renderSettings.Fps);
                renderSettings.EndInBars = (float)SecondsToReferenceTime(endInSeconds, renderSettings.Reference, renderSettings.Fps);
                break;
            }

            case RenderSettings.TimeRanges.Soundtrack:
            {
                if (PlaybackUtils.TryFindingSoundtrack(out var handle, out _))
                {
                    var playback = Playback.Current;
                    var clip = handle.Clip;
                    renderSettings.StartInBars = (float)SecondsToReferenceTime(playback.SecondsFromBars(clip.StartTime), renderSettings.Reference, renderSettings.Fps);
                    if (clip.EndTime > 0)
                        renderSettings.EndInBars = (float)SecondsToReferenceTime(playback.SecondsFromBars(clip.EndTime), renderSettings.Reference, renderSettings.Fps);
                    else
                        renderSettings.EndInBars = (float)SecondsToReferenceTime(clip.LengthInSeconds, renderSettings.Reference, renderSettings.Fps);
                }
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(range), range, null);
        }
    }

    public static double ConvertReferenceTime(double time, RenderSettings.TimeReference oldRef, RenderSettings.TimeReference newRef, float fps)
    {
        if (oldRef == newRef)
            return time;

        var seconds = ReferenceTimeToSeconds(time, oldRef, fps);
        return SecondsToReferenceTime(seconds, newRef, fps);
    }

    public static double ConvertFps(double time, double oldFps, double newFps)
    {
        if (oldFps == 0 || newFps == 0) return time;
        return time / oldFps * newFps;
    }

    public static double ReferenceTimeToSeconds(double time, RenderSettings.TimeReference reference, float fps)
    {
        var playback = Playback.Current;
        switch (reference)
        {
            case RenderSettings.TimeReference.Bars:
                return playback.SecondsFromBars(time);
            case RenderSettings.TimeReference.Seconds:
                return time;
            case RenderSettings.TimeReference.Frames:
                return fps != 0 ? time / fps : time / 60.0;
            default:
                return time;
        }
    }

    public static double SecondsToReferenceTime(double seconds, RenderSettings.TimeReference reference, float fps)
    {
        var playback = Playback.Current;
        switch (reference)
        {
            case RenderSettings.TimeReference.Bars:
                return playback.BarsFromSeconds(seconds);
            case RenderSettings.TimeReference.Seconds:
                return seconds;
            case RenderSettings.TimeReference.Frames:
                return fps != 0 ? seconds * fps : seconds * 60.0;
            default:
                return seconds;
        }
    }

    public static int ComputeFrameCount(in RenderSettings s)
    {
        var start = ReferenceTimeToSeconds(s.StartInBars, s.Reference, s.Fps);
        var end = ReferenceTimeToSeconds(s.EndInBars, s.Reference, s.Fps);
        return (int)Math.Round((end - start) * s.Fps);
    }

    public static void SetPlaybackTimeForFrame(ref RenderSettings s, int frameIndex, int frameCount, ref Runtime rt)
    {
        // get playback settings
        var composition = T3.Editor.UiModel.ProjectHandling.ProjectView.Focused?.CompositionInstance;
        if (composition == null)
        {
            Log.Warning("Can't find focused composition instance.");
            return;
        }

        PlaybackUtils.FindPlaybackSettingsForInstance(composition, out var instanceWithSettings, out var playbackSettings);

        // change global settings before calculating times
        Playback.Current.Bpm = playbackSettings.Bpm;
        Playback.Current.PlaybackSpeed = 0.0;
        Playback.Current.Settings = playbackSettings;
        Playback.Current.FrameSpeedFactor = s.Fps / 60.0f;

        // time range
        var startSecs = ReferenceTimeToSeconds(s.StartInBars, s.Reference, s.Fps);
        var endSecs = startSecs + Math.Max(frameCount - 1, 0) / s.Fps;

        var oldSecs = Playback.Current.TimeInSecs;
        var progress = frameCount <= 1 ? 0.0 : frameIndex / (double)(frameCount - 1);
        Playback.Current.TimeInSecs = MathUtils.Lerp(startSecs, endSecs, progress);
        var adaptedDelta = Math.Max(Playback.Current.TimeInSecs - oldSecs + rt.TimingOverhang, 0.0);

        // audio clip for preview
        if (playbackSettings.TryGetMainSoundtrack(instanceWithSettings, out var soundtrack))
            AudioEngine.UseAudioClip(soundtrack, Playback.Current.TimeInSecs);

        if (!rt.AudioRecording)
        {
            rt.TimingOverhang = 0.0;
            adaptedDelta = 1.0 / s.Fps;

            Playback.Current.IsRenderingToFile = true;
            Playback.Current.PlaybackSpeed = 1.0;

            AudioRendering.PrepareRecording(Playback.Current, s.Fps);

            var requestedEndSecs = ReferenceTimeToSeconds(s.EndInBars, s.Reference, s.Fps);
            var actualEndSecs = startSecs + ComputeFrameCount(s) / s.Fps;

            Log.Debug($"Requested recording from {startSecs:0.0000} to {requestedEndSecs:0.0000} seconds");
            Log.Debug($"Actually recording from {startSecs:0.0000} to {actualEndSecs:0.0000} seconds due to frame raster");
            Log.Debug($"Using {Playback.Current.Bpm} bpm");

            rt.AudioRecording = true;
        }

        // update audio engine (respect looping etc.)
        Playback.Current.Update();

        var bufferLengthMs = (int)Math.Floor(1000.0 * adaptedDelta);
        rt.TimingOverhang = adaptedDelta - bufferLengthMs / 1000.0;
        rt.TimingOverhang = Math.Max(rt.TimingOverhang, 0.0);

        AudioEngine.CompleteFrame(Playback.Current, bufferLengthMs / 1000.0);
    }

    public static void ReleasePlaybackTime(ref RenderSettings s, ref Runtime rt)
    {
        AudioRendering.EndRecording(Playback.Current, s.Fps);

        Playback.Current.TimeInSecs = ReferenceTimeToSeconds(s.EndInBars, s.Reference, s.Fps);
        Playback.Current.IsRenderingToFile = false;
        Playback.Current.PlaybackSpeed = 0.0;
        Playback.Current.FrameSpeedFactor = 1.0; // could use actual display frame rate
        Playback.Current.Update();

        rt.AudioRecording = false;
        rt.TimingOverhang = 0.0;
    }
}
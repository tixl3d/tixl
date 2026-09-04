using T3.Core.Animation;

namespace T3.Editor.Gui.Interaction.Timing;

/// <summary>
/// Override the default Playback to support continuous playback synchronized to BPM.
/// This basically joins Core.Playback with Editor.BeatTiming.
/// </summary>
internal sealed class BeatTimingPlayback : Playback
{
    public override void Update(bool idleMotionEnabled = false)
    {
        FrameCount++;

        var currentRuntimeInSecs = IsRenderingToFile ?   TimeInSecs : RunTimeInSecs;

        LastFrameDuration = (float)(currentRuntimeInSecs - _lastFrameStart);
        _lastFrameStart = currentRuntimeInSecs;
            
        FxTimeInBars = BeatTiming.BeatTime;
        Bpm = BeatTiming.Bpm;
        TimeInBars = FxTimeInBars;
    }
        
    private static double _lastFrameStart;
}
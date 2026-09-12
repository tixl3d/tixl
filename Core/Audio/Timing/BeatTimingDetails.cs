namespace T3.Core.Audio.Timing;

/// <summary>
/// A helper class to provides current status details of the beat tapping / and timing offsets.
/// This is mainly used by the editor to publish results to Operator space.
/// </summary>
public static class BeatTimingDetails
{
    public static float DistanceToMeasure;
    public static float DistanceToBeat;
        
    public static float MeasureDuration;
    public static float BeatDurationInSecs;
    public static float SlideOffsetInSecs;
    public static float WasTapTriggered;
    public static float WasResyncTriggered;
    public static float Bpm;
    public static float SyncMeasureOffset;
    public static float BeatTime;

    public static float LastPhaseOffset;
    public static float BarSync;
    public static float LastTapDistanceToBeat;
}
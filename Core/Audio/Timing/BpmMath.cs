namespace T3.Core.Audio.Timing;

/// <summary>
/// Conversions between tempo and bar time for four beats per bar, so the factor 240 lives in one place.
/// </summary>
internal static class BpmMath
{
    public static double BarsPerSecond(double bpm) => bpm / 240.0;
    public static double BarDurationSeconds(double bpm) => 240.0 / bpm;
    public static double BpmFromBarDuration(double barDurationSeconds) => 240.0 / barDurationSeconds;
}

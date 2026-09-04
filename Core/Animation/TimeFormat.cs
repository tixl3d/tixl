namespace T3.Core.Animation;

public static class TimeFormat
{
    private static int GetBeatTimeBar(double timeInBars, int startCountFrom)
    {
        return (int)(timeInBars) + startCountFrom;   // NOTE:  We count bars from Zero because it matches the current time
    }

    private static int GetBeatTimeBeat(double timeInBars, int startCountFrom)
    {
        return (int)(timeInBars * 4) % 4 + startCountFrom;
    }

    private static int GetBeatTimeTick(double timeInBars, int startCountFrom)
    {
        return (int)(timeInBars * 16) % 4 + startCountFrom;
    }

    public static string FormatTimeInBars(double timeInBars, int startCountFrom)
    {
        return $"{GetBeatTimeBar(timeInBars, startCountFrom):0}.{GetBeatTimeBeat(timeInBars, startCountFrom):0}.{GetBeatTimeTick(timeInBars,startCountFrom):0}.";
    }

    public static double ToSeconds(double timeInBars, double bpm)
    {
        return timeInBars * 240 / bpm;
    }

    public enum TimeDisplayModes
    {
        Secs,
        Bars,
        F30,
        F60,
    }
}
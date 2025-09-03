using System;

namespace T3.Core.IO;

public interface ITapProvider
{
    public bool BeatTapTriggered { get; }
    public bool ResyncTriggered { get; }
    public float SlideSyncTime { get; }
}

public sealed class TapProvider : ITapProvider
{
    public static readonly TapProvider Instance = new();

    private TapProvider()
    {
        if(Instance != null)
            throw new Exception("TapProvider is a singleton and should not be instantiated more than once");
    }
    
    public bool BeatTapTriggered { get; set; }
    public bool ResyncTriggered { get; set; }
    public float SlideSyncTime { get; set; }
}
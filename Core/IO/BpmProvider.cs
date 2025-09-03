using System;

namespace T3.Core.IO;

public interface IBpmProvider
{
    public bool TryGetNewBpmRate(out float bpm);
}

public sealed class BpmProvider : IBpmProvider
{
    public static readonly BpmProvider Instance = new();
    
    private BpmProvider()
    {
        
        if(Instance != null)
            throw new Exception("BpmProvider is a singleton and should not be instantiated more than once");
    }

    // This will be process every frame by the editor
    public bool TryGetNewBpmRate(out float bpm)
    {
        if (!SetBpmTriggered)
        {
            bpm = NewBpmRate;
            return false;
        }

        SetBpmTriggered = false;
        bpm = NewBpmRate;
        return true;
    }
    
    public bool SetBpmTriggered;
    public float NewBpmRate;
    public bool TriggerUpdate;
}
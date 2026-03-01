using System;
using System.Runtime.CompilerServices;

namespace T3.Core.Operator.Slots;

/// <summary>
/// Manages a version-chasing synchronization where SourceVersion tracks the latest available data state and
/// ValueVersion tracks the version of the currently cached value. A slot is considered "dirty" whenever SourceVersion > ValueVersion,
/// triggering an update that recalculates the logic and synchronizes the two counters. This mechanism allows the engine to skip redundant
/// calculations by ensuring that work is only performed when a dependency has actually incremented its version.
/// </summary>
public sealed class DirtyFlag
{
    public static void IncrementGlobalTicks()
    {
        _globalTickCount += GlobalTickDiffPerFrame;
    }

    public bool IsDirty => TriggerIsEnabled || ValueVersion != SourceVersion;

    public static int GlobalInvalidationTick = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Invalidate()
    {
        // If we already visited this slot during the current Invalidation Pass, stop.
        if (InvalidationTick == GlobalInvalidationTick) 
            return SourceVersion;
        
        InvalidationTick = GlobalInvalidationTick;
        SourceVersion++;

        return SourceVersion;
    }

    public void ForceInvalidate()
    {
        InvalidationTick = GlobalInvalidationTick;
        SourceVersion++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        ValueVersion = SourceVersion;
    }

    // editor-specific function
    internal void SetUpdated()
    {
        if (_lastUpdateTick >= _globalTickCount && _lastUpdateTick < _globalTickCount + GlobalTickDiffPerFrame - 1)
        {
            _lastUpdateTick++;
        }
        else
        {
            _lastUpdateTick = _globalTickCount;
        }
    }

    public int ValueVersion;
    public int SourceVersion = 1; // initially dirty

    // editor-specific value
    public int FramesSinceLastUpdate => (_globalTickCount - 1 - _lastUpdateTick) / GlobalTickDiffPerFrame;

    public DirtyFlagTrigger Trigger
    {
        get => _trigger;
        set
        {
            _trigger = value;
            TriggerIsEnabled = value != DirtyFlagTrigger.None;
            TriggerIsAnimated = value == DirtyFlagTrigger.Animated;
        }
    }

    private DirtyFlagTrigger _trigger;
    internal bool TriggerIsEnabled;
    internal bool TriggerIsAnimated;

    // editor-specific value
    public int NumUpdatesWithinFrame
    {
        get
        {
            var updatesSinceLastFrame = _lastUpdateTick - _globalTickCount;
            var shift = (-updatesSinceLastFrame >= GlobalTickDiffPerFrame) ? GlobalTickDiffPerFrame : 0;
            return Math.Max(updatesSinceLastFrame + shift + 1, 0); // shift corrects if update was one frame ago
        }
    }

    internal int InvalidationTick = -1;
    private const int GlobalTickDiffPerFrame = 100; // each frame differs with 100 ticks to last one
    private static int _globalTickCount;
    private int _lastUpdateTick;
}
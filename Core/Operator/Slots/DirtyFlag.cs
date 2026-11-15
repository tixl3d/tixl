using System;
using System.Runtime.CompilerServices;

using System.Collections.Generic;

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

    /** Slots the current invalidation walk actually descended into — how far a "mark everything dirty" reaches. */
    public static int InvalidationVisits;

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

    /// <summary>Whether the slot ran its update since the ticks were advanced for this frame — i.e. some other
    /// consumer already evaluated it, and a view can show the value without invalidating the graph again.</summary>
    public bool WasUpdatedThisFrame => _lastUpdateTick >= _globalTickCount;

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

    /// <summary>
    /// Editor-specific: how often the slot updated in the current frame, or in the previous one if it hasn't
    /// updated yet in this one. Each update within a frame advances the tick by one from the frame's start tick.
    /// </summary>
    public int NumUpdatesWithinFrame
    {
        get
        {
            var ticksSinceFrameStart = _lastUpdateTick - _globalTickCount;
            if (ticksSinceFrameStart >= 0)
                return ticksSinceFrameStart + 1;

            var ticksSincePreviousFrameStart = ticksSinceFrameStart + GlobalTickDiffPerFrame;
            return ticksSincePreviousFrameStart >= 0 ? ticksSincePreviousFrameStart + 1 : 0;
        }
    }

    internal int InvalidationTick = -1;

    /// <summary>
    /// The <see cref="GlobalInvalidationTick"/> of the last invalidation pass that reached this slot. A pass walks
    /// everything it evaluates, unchanged parts included, so this tells whether anything still pulls the slot even
    /// when its value has not been updated for a long time.
    /// </summary>
    public int LastInvalidationTick => InvalidationTick;
    private const int GlobalTickDiffPerFrame = 100; // each frame differs with 100 ticks to last one
    private static int _globalTickCount;
    private int _lastUpdateTick;
}
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.Gui.Interaction.Variations;

/// <summary>
/// Forwards abstract actions triggered by midi-inputs to actions on variations/snapshots.
/// 
/// Crossfader model:
/// - _snapshotLeft: snapshot at crossfader position 0
/// - _snapshotRight: snapshot at crossfader position 127
/// - _activeIsLeft: true if the "active" snapshot is on the left side
/// 
/// The blend target is always the opposite side from active.
/// When we reach an endpoint, that side becomes active.
/// </summary>
public static class BlendActions
{
    /// <summary>
    /// Sets a new blend target. The current active snapshot stays where it is,
    /// and the new target is placed on the opposite side.
    /// </summary>
    public static void StartBlendingTowardsSnapshot(int index)
    {
        if (VariationHandling.ActiveInstanceForSnapshots == null || VariationHandling.ActivePoolForSnapshots == null)
        {
            Log.Warning("Can't blend without active composition or variation pool");
            return;
        }

        if (!SymbolVariationPool.TryGetSnapshot(index, out var variation))
            return;

        // Get the current active snapshot
        var currentActive = _activeIsLeft ? _snapshotLeft : _snapshotRight;
        
        // If no active snapshot yet, use the target as both (edge case)
        if (currentActive == -1)
        {
            _snapshotLeft = index;
            _snapshotRight = index;
            _activeIsLeft = true;
        }
        else
        {
            // Keep the current active where it is, place target on opposite side
            if (_activeIsLeft)
            {
                // Active is on left, put new target on right
                _snapshotRight = index;
            }
            else
            {
                // Active is on right, put new target on left
                _snapshotLeft = index;
            }
        }
        
        VariationHandling.ActivePoolForSnapshots.BeginBlendTowardsSnapshot(
            VariationHandling.ActiveInstanceForSnapshots, variation, 0);
    }

    public static void UpdateBlendingTowardsProgress(int index, float midiValue)
    {
        if (VariationHandling.ActiveInstanceForSnapshots == null || VariationHandling.ActivePoolForSnapshots == null)
        {
            Log.Warning("Can't blend without active composition or variation pool");
            return;
        }

        // Need both endpoints defined for blending
        if (_snapshotLeft == -1 || _snapshotRight == -1)
        {
            return;
        }

        // Crossfader position: 0 = left, 127 = right
        var normalizedPosition = midiValue / 127.0f;
        
        // Check if we've reached the right endpoint
        if (normalizedPosition >= 0.99f)
        {
            FinishAtRight();
            return;
        }
        
        // Check if we've reached the left endpoint  
        if (normalizedPosition <= 0.01f)
        {
            FinishAtLeft();
            return;
        }

        // We're somewhere in the middle - perform the blend
        // Blend toward the target (opposite of active side)
        var targetSnapshot = _activeIsLeft ? _snapshotRight : _snapshotLeft;
        var blendAmount = _activeIsLeft ? normalizedPosition : (1.0f - normalizedPosition);
        
        if (SymbolVariationPool.TryGetSnapshot(targetSnapshot, out var targetVariation))
        {
            SmoothVariationBlending.StartBlendTo(targetVariation, blendAmount);
        }
        else
        {
            SmoothVariationBlending.Stop();
        }
    }
    
    /// <summary>
    /// Called when crossfader reaches the right side (position 127).
    /// </summary>
    private static void FinishAtRight()
    {
        // Request completion - actual completion happens when damping finishes
        _pendingCompletion = CompletionSide.Right;
    }
    
    /// <summary>
    /// Called when crossfader reaches the left side (position 0).
    /// </summary>
    private static void FinishAtLeft()
    {
        // Request completion - actual completion happens when damping finishes
        _pendingCompletion = CompletionSide.Left;
    }
    
    /// <summary>
    /// Actually completes the blend after damping has finished.
    /// </summary>
    internal static void CompleteBlendWhenDampingFinished()
    {
        if (_pendingCompletion == CompletionSide.None)
            return;
            
        var completingSide = _pendingCompletion;
        _pendingCompletion = CompletionSide.None;
        
        VariationHandling.ActivePoolForSnapshots?.ApplyCurrentBlend();
        
        if (completingSide == CompletionSide.Right)
        {
            VariationHandling.ActivePoolForSnapshots?.UpdateActiveStateForVariation(_snapshotRight);
            _activeIsLeft = false;
        }
        else
        {
            VariationHandling.ActivePoolForSnapshots?.UpdateActiveStateForVariation(_snapshotLeft);
            _activeIsLeft = true;
        }
        
        SmoothVariationBlending.Stop();
    }
    
    private enum CompletionSide { None, Left, Right }
    private static CompletionSide _pendingCompletion = CompletionSide.None;
    
    public static void StopBlendingTowards()
    {
        _snapshotRight = -1;
        VariationHandling.ActivePoolForSnapshots?.ApplyCurrentBlend();
        SmoothVariationBlending.Stop();
    }
    
    /// <summary>
    /// Sets the active snapshot index (called when a snapshot is directly activated, not blended).
    /// </summary>
    public static void SetActiveSnapshot(int index)
    {
        _snapshotLeft = index;
        _activeIsLeft = true;
        // Clear right side - user needs to set a new blend target
        _snapshotRight = -1;
    }
    
    /// <summary>
    /// Gets the current blend target index, or -1 if none.
    /// The target is always the opposite side from active.
    /// </summary>
    public static int BlendTowardsIndex
    {
        get
        {
            if (_snapshotLeft == -1 || _snapshotRight == -1)
                return -1;
            return _activeIsLeft ? _snapshotRight : _snapshotLeft;
        }
    }
    
    /// <summary>
    /// Gets the currently active snapshot index, or -1 if none.
    /// </summary>
    public static int ActiveSnapshotIndex
    {
        get
        {
            if (_snapshotLeft == -1 && _snapshotRight == -1)
                return -1;
            return _activeIsLeft ? _snapshotLeft : _snapshotRight;
        }
    }
    
    public static void UpdateBlendValues(int obj, float value)
    {
        //Log.Warning($"BlendValuesUpdate {obj} not implemented");
    }

    public static void StartBlendingSnapshots(int[] indices)
    {
        Log.Warning($"StartBlendingSnapshots {indices.Length} not implemented");
    }
    
    /// <summary>
    /// Smooths blending between variations to avoid glitches by low 127 midi resolution steps 
    /// </summary>
    public static class SmoothVariationBlending
    {
        public static void StartBlendTo(Variation variation, float normalizedBlendWeight)
        {
            if (variation != _targetVariation)
            {
                _dampedWeight = normalizedBlendWeight;
                _targetVariation = variation;
            }

            _targetWeight = normalizedBlendWeight;
            UpdateBlend();
        }

        public static void UpdateBlend()
        {
            if (_targetVariation == null || VariationHandling.ActiveInstanceForSnapshots == null)
                return;

            if (float.IsNaN(_dampedWeight) || float.IsInfinity(_dampedWeight))
            {
                _dampedWeight = _targetWeight;
            }

            if (float.IsNaN(_dampingVelocity) || float.IsInfinity(_dampingVelocity))
            {
                _dampingVelocity = 0.5f;
            }

            var frameDuration = 1 / 60f;    // Fixme: (float)Playback.LastFrameDuration
            _dampedWeight = MathUtils.SpringDamp(_targetWeight,
                                                 _dampedWeight,
                                                 ref _dampingVelocity,
                                                 20f, frameDuration);

            // Check if damping has settled
            if (MathF.Abs(_dampingVelocity) < 0.0005f)
            {
                // Damping finished - complete any pending blend completion
                CompleteBlendWhenDampingFinished();
                return;
            }

            VariationHandling.ActivePoolForSnapshots?.
                              BeginBlendTowardsSnapshot(VariationHandling.ActiveInstanceForSnapshots, 
                                                        _targetVariation, 
                                                        _dampedWeight);
        }

        public static void Stop()
        {
            _targetVariation = null;
        }

        private static float _targetWeight;
        private static float _dampedWeight;
        private static float _dampingVelocity;
        private static Variation _targetVariation;
    }

    // Crossfader endpoints
    private static int _snapshotLeft = -1;   // Snapshot at position 0
    private static int _snapshotRight = -1;  // Snapshot at position 127
    private static bool _activeIsLeft = true; // Which side is currently "active"
}
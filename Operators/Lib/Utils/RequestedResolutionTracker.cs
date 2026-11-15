using System;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output.Rendering;

namespace Lib.Utils;

/// <summary>
/// Keeps the outputs of an op that reads <see cref="EvaluationContext.RequestedResolution"/> cached until the
/// resolution changes, instead of re-evaluating them every frame. A change is reported by whoever sets a resolution
/// (<see cref="EvaluationContext.RequestedResolutionChanged"/>) or noticed here when the op reads a different value.
/// </summary>
internal sealed class RequestedResolutionTracker : IDisposable
{
    public RequestedResolutionTracker(params ISlot[] outputs)
    {
        _outputs = outputs;
        EvaluationContext.RequestedResolutionChanged += OnResolutionChanged;
    }

    /// <summary>Call from the op's update with the resolution it read from the context.</summary>
    public void Track(Int2 resolution)
    {
        _isTracking = true;
        if (resolution != _lastResolution)
        {
            _lastResolution = resolution;
            _lastChangeFrame = OutputFrame.Index;
            SetTriggers(DirtyFlagTrigger.Animated);
        }
        else if (OutputFrame.Index - _lastChangeFrame > 1)
        {
            // Cached only after a whole frame without change: an instance pulled at two sizes within one frame
            // (two render targets, an output and a preview) sees a change on every pull and stays animated.
            SetTriggers(DirtyFlagTrigger.None);
        }
    }

    /// <summary>Call from the update when the op no longer reads the context's resolution.</summary>
    public void StopTracking()
    {
        if (!_isTracking)
            return;

        _isTracking = false;
        SetTriggers(DirtyFlagTrigger.None);
    }

    public void Dispose()
    {
        EvaluationContext.RequestedResolutionChanged -= OnResolutionChanged;
    }

    private void OnResolutionChanged()
    {
        if (!_isTracking)
            return;

        _lastChangeFrame = OutputFrame.Index;
        SetTriggers(DirtyFlagTrigger.Animated);
    }

    private void SetTriggers(DirtyFlagTrigger trigger)
    {
        foreach (var output in _outputs)
        {
            output.DirtyFlag.Trigger = trigger;
        }
    }

    private readonly ISlot[] _outputs;
    private Int2 _lastResolution;
    private int _lastChangeFrame;
    private bool _isTracking;
}

using System;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Settings;

// ReSharper disable ForCanBeConvertedToForeach

namespace T3.Core.Operator.Slots;

public interface ITimeClipProvider
{
    TimeClip TimeClip { get; }
}

public interface IOutputDataUser
{
    void SetOutputData(IOutputData data);
}

// This interface is mainly to extract the output data type while no instance of an implementer exists.
internal interface IOutputDataUser<T> : IOutputDataUser
{
}

/// <summary>
/// An unfortunate workaround to allow flagging operators that should not update their source time region when being dragged
/// in the timeline. This primarily useful for ops like [TimeClip] that do not involve content nested in their sub elements.
/// Also see <see cref="TimeClip.UsedForRegionMapping"/>.
/// </summary>
public interface IPreventingTimeRemap;

/// <summary>
/// Marks a clip whose <see cref="TimeClip.SourceRange"/> is <b>content-time</b> — anchored at the source's start
/// (0), not the placement position. The editor uses this at creation to default SourceRange to <c>[0, duration]</c>
/// instead of the generic <c>SourceRange = TimeRange</c> (which is right only for region-mapping clips). Implemented
/// by media clips like [VideoClip]. (Future: pair with an instance-side AvailableSourceRange the editor can snap to.)
/// </summary>
public interface IContentTimeClip;

public sealed class TimeClipSlot<T> : Slot<T>, ITimeClipProvider, IOutputDataUser<TimeClip>
{
    public TimeClip TimeClip { get; private set; }

    /// <summary>
    /// Opts out of the out-of-range gate: the slot then always evaluates and only remaps time. For media
    /// clips whose content must stay pullable outside the clip window (decoder preroll, first/last-frame
    /// clamp) — the op is responsible for clamping to its source range.
    /// </summary>
    public bool EvaluateOutsideRange;

    public TimeClipSlot()
    {
        HasInvalidationOverride = true;
    }

    public void SetOutputData(IOutputData data)
    {
        TimeClip = data as TimeClip ?? new TimeClip();
        TimeClip.Id = Parent.SymbolChildId;
        TimeClip.UsedForRegionMapping = Parent is not IPreventingTimeRemap;
    }

    public UpdateStates LastUpdateStatus;

    private void UpdateWithTimeRangeCheck(EvaluationContext context)
    {
        if (!EvaluateOutsideRange
            && ((context.LocalTime < TimeClip.TimeRange.Start) || (context.LocalTime >= TimeClip.TimeRange.End)))
        {
            LastUpdateStatus = CoreSettings.Config.TimeClipSuspending ? UpdateStates.Suspended : UpdateStates.Active;
            return;
        }

        // TODO: Setting local time should flag time accessors as dirty
        var prevTime = context.LocalTime;
        var prevFxTime = context.LocalFxTime;

        context.LocalTime = TimeClip.MapTimelineToSource(prevTime);
        context.LocalFxTime = TimeClip.MapTimelineToSource(prevFxTime);

        if (_baseUpdateAction == null)
        {
            Log.Warning("Ignoring invalid time clip update action", Parent);
        }
        else
        {
            _baseUpdateAction(context);
        }

        context.LocalTime = prevTime;
        context.LocalFxTime = prevFxTime;
        LastUpdateStatus = UpdateStates.Active;
    }

    private Action<EvaluationContext> _baseUpdateAction;

    public enum UpdateStates
    {
        Undefined,
        Active,
        Inactive, // Out of range
        Suspended,
    }

    public override Action<EvaluationContext> UpdateAction
    {
        set
        {
            // A null assignment (e.g. RestoreUpdateAction after a recompile) must clear the wrapper too —
            // keeping it would leave a slot that warns "invalid time clip update action" every frame.
            _baseUpdateAction = value;
            base.UpdateAction = value == null ? null : UpdateWithTimeRangeCheck;
        }
    }

    protected override void SetDisabled(bool isDisabled)
    {
        if (isDisabled == _isDisabled)
            return;

        if (isDisabled)
        {
            _keepOriginalUpdateAction = _baseUpdateAction;
            // Must be stashed like the base disable path does: RestoreUpdateAction writes it back on
            // re-enable, and without the stash it wipes the trigger (e.g. Animated) — the slot then
            // never re-evaluates after re-enabling.
            _keepDirtyFlagTrigger = _dirtyFlag.Trigger;
            base.UpdateAction = EmptyAction;
            DirtyFlag.Invalidate();
        }
        else
        {
            RestoreUpdateAction();
            DirtyFlag.Invalidate();
        }

        _isDisabled = isDisabled;
    }

    protected override int InvalidationOverride()
    {
        // Slot is an output of a composition op
        if (HasInputConnections)
        {
            return InputConnections[0].InvalidateGraph();
        }

        if (LastUpdateStatus == UpdateStates.Suspended)
        {
            return _dirtyFlag.Invalidate();
        }

        var isOutputDirty = _dirtyFlag.IsDirty;
        var parentInputs = Parent.Inputs;
        var parentInputCount = parentInputs.Count;
        for (var index = 0; index < parentInputCount; index++)
        {
            var inputSlot = parentInputs[index];
            // Let each input handle its own invalidation strategy (e.g. MultiInputSlot traverses all collected connections).
            inputSlot.InvalidateGraph();
            isOutputDirty |= inputSlot.IsDirty;
        }

        return isOutputDirty ? _dirtyFlag.Invalidate() : _dirtyFlag.SourceVersion;
    }
}
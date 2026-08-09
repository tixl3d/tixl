#nullable enable
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator.Interfaces;
using T3.Core.Stats;
using T3.Core.Utils;
// ReSharper disable FieldCanBeMadeReadOnly.Local
// ReSharper disable InconsistentNaming

// ReSharper disable ConvertToAutoPropertyWhenPossible
// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable InlineTemporaryVariable

namespace T3.Core.Operator.Slots;

/// <summary>
/// Lets instantiation code hand a child's <see cref="Animation.TimeClip"/> to its non-clip output
/// slots without knowing the slot's generic type. See <see cref="TimeClipSlot{T}"/>.
/// </summary>
internal interface ITimeClipRemapTarget
{
    void SetTimeClipForOutputRemap(Animation.TimeClip timeClip);
}

public class Slot<T> : ISlot, ITimeClipRemapTarget
{
    public Guid Id;
    private readonly Type _valueType;
    Type ISlot.ValueType => _valueType;
    
    public Instance Parent
    {
        get => _parent;
        set
        {
            _parent = value;
            _parentIsICompoundWithUpdate = _parent is ICompoundWithUpdate;
        }
    }

    public DirtyFlag DirtyFlag => _dirtyFlag;
    // ReSharper disable once FieldCanBeMadeReadOnly.Local
    private protected DirtyFlag _dirtyFlag = new();
        
    public T Value;

    protected bool _isDisabled;

    protected virtual void SetDisabled(bool shouldBeDisabled)
    {
        if (shouldBeDisabled == _isDisabled)
            return;

        if (shouldBeDisabled)
        {
            if (_keepOriginalUpdateAction != null)
            {
                Log.Warning("Is already bypassed or disabled");
                return;
            }
                
            _keepOriginalUpdateAction = UpdateAction;
            _keepDirtyFlagTrigger = _dirtyFlag.Trigger;
            UpdateAction = EmptyAction;
            _dirtyFlag.Invalidate();
        }
        else
        {
            RestoreUpdateAction();
        }

        _isDisabled = shouldBeDisabled;
    }

    internal bool TryGetAsMultiInputTyped(out MultiInputSlot<T> multiInput)
    {
        multiInput = _thisAsMultiInputSlot;
        return _isMultiInput;
    }

    internal virtual bool TrySetBypassToInput(Slot<T> targetSlot)
    {
        if (_keepOriginalUpdateAction != null)
        {
            //Log.Warning("Already disabled or bypassed");
            return false;
        }
            
        _keepOriginalUpdateAction = UpdateAction;
        _keepDirtyFlagTrigger = _dirtyFlag.Trigger;
        UpdateAction = ByPassUpdate;
        _dirtyFlag.Invalidate();
        _targetInputForBypass = targetSlot;
        return true;
    }

    internal void OverrideWithAnimationAction(Action<EvaluationContext> newAction)
    {
        // Animation actions are updated regardless if operator was already animated
        if (_keepOriginalUpdateAction == null)
        {
            _keepOriginalUpdateAction = UpdateAction;
            _keepDirtyFlagTrigger = _dirtyFlag.Trigger;
        }

        UpdateAction = newAction;
        _dirtyFlag.Invalidate();
    }
        
    public virtual void RestoreUpdateAction()
    {
        // This will happen when operators are recompiled and output slots are InputConnections[0]
        if (_keepOriginalUpdateAction == null)
        {
            UpdateAction = null;
            return;
        }
            
        UpdateAction = _keepOriginalUpdateAction;
        _keepOriginalUpdateAction = null;
        _dirtyFlag.Trigger = _keepDirtyFlagTrigger;
        _dirtyFlag.Invalidate();
    }

    public bool IsDisabled 
    {
        get => _isDisabled;
        set => SetDisabled(value);
    }

    // ReSharper disable once StaticMemberInGenericType
    protected static readonly Action<EvaluationContext> EmptyAction = _ => { };

    public Slot()
    {
        Value = default!;  // tells the compiler it's intentionally initialized
        
        // UpdateAction = Update;
        _valueType = typeof(T);
        _valueIsCommand = _valueType == typeof(Command);
            
        if (this is IInputSlot)
        {
            _isInputSlot = true;
        }
    }

    public Slot(T defaultValue) : this()
    {
        Value = defaultValue;
    }
        
    // dummy constructor to initialize input slot values
    // ReSharper disable once UnusedParameter.Local
    protected Slot(bool _) : this()
    {
        _isInputSlot = true;
        if (this is MultiInputSlot<T> multiInputSlot)
        {
            _isMultiInput = true;
            _thisAsMultiInputSlot = multiInputSlot;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Update(EvaluationContext context)
    {
        if (_dirtyFlag.IsDirty || _valueIsCommand)
        {
            OpUpdateCounter.CountUp();
            _effectiveUpdateAction?.Invoke(context);
            _dirtyFlag.Clear();
            _dirtyFlag.SetUpdated();
        }
    }

    void ITimeClipRemapTarget.SetTimeClipForOutputRemap(Animation.TimeClip timeClip)
    {
        _timeClipForOutputRemap = timeClip;
        RebuildEffectiveUpdateAction();
    }

    /// <summary>
    /// A time clip remaps time for the whole operator, so sibling outputs of a <see cref="TimeClipSlot{T}"/>
    /// get the same source-time remap — but without the out-of-range gate, so consumers can pre-roll
    /// content slightly outside the clip (e.g. warming a video decoder before the cut).
    /// The wrap is baked into <see cref="_effectiveUpdateAction"/> here, at assignment time, so
    /// <see cref="Update"/> stays a single delegate invocation for every slot.
    /// </summary>
    private void RebuildEffectiveUpdateAction()
    {
        var action = _updateAction;
        if (_timeClipForOutputRemap == null || action == null)
        {
            _effectiveUpdateAction = action;
            return;
        }

        var timeClip = _timeClipForOutputRemap;
        _effectiveUpdateAction = context =>
                                 {
                                     var prevTime = context.LocalTime;
                                     var prevFxTime = context.LocalFxTime;
                                     context.LocalTime = timeClip.MapTimelineToSource(prevTime);
                                     context.LocalFxTime = timeClip.MapTimelineToSource(prevFxTime);

                                     action(context);

                                     context.LocalTime = prevTime;
                                     context.LocalFxTime = prevFxTime;
                                 };
    }

    public void ConnectedUpdate(EvaluationContext context)
    {
        Value = InputConnections[0].GetValue(context)!;
    }
        
    protected void ByPassUpdate(EvaluationContext context)
    {
        Value = _targetInputForBypass!.GetValue(context)!;
    }

    public T? GetValue(EvaluationContext context)
    {
        Update(context);

        return Value;
    }

    public void AddConnection(ISlot sourceSlot, int index = 0)
    {
        if (!HasInputConnections)
        {
            if (UpdateAction != null)
            {
                _actionBeforeAddingConnecting = UpdateAction;
                if (_parentIsICompoundWithUpdate && !_isInputSlot && _parent.Children.Count > 0)
                {
                    ArrayUtils.InsertAtIndexOrEnd(ref InputConnections, (Slot<T>)sourceSlot, index);
                    _dirtyFlag.SourceVersion = sourceSlot.DirtyFlag.SourceVersion;
                    _dirtyFlag.ValueVersion = - 1;
                    return;
                }
            }
            UpdateAction = ConnectedUpdate;
            _dirtyFlag.SourceVersion = sourceSlot.DirtyFlag.SourceVersion;
            _dirtyFlag.ValueVersion = -1;
        }
            
        if (sourceSlot.ValueType != _valueType)
        {
            Log.Warning("Type mismatch during connection");
            return;
        }

        ArrayUtils.InsertAtIndexOrEnd(ref InputConnections, (Slot<T>)sourceSlot, index);
    }

    private Action<EvaluationContext>? _actionBeforeAddingConnecting;

    public void RemoveConnection(int index = 0)
    {
        if (HasInputConnections)
        {
            if (index < InputConnections.Length)
            {
                ArrayUtils.RemoveAt(ref InputConnections, index);
            }
            else
            {
                Log.Error($"{Parent} trying to delete connection at index {index}, but {GetType()} only has {InputConnections.Length} connections");
            }
        }

        if (!HasInputConnections)
        {
            if (_actionBeforeAddingConnecting != null)
            {
                UpdateAction = _actionBeforeAddingConnecting;
            }
            else
            {
                // if no connection is set anymore restore the default update action
                RestoreUpdateAction();
            }
            _dirtyFlag.ForceInvalidate();
        }
    }

    public bool HasInputConnections => InputConnections.Length > 0;

    public ISlot? FirstConnection => InputConnections.Length > 0 ? InputConnections[0] : null;

    public bool TryGetFirstConnection(out ISlot? connectedSlot)
    {
        if(InputConnections.Length > 0)
        {
            connectedSlot = InputConnections[0];
            return true;
        }
            
        connectedSlot = null;
        return false;
    }

    protected Slot<T>[] InputConnections = [];

    public int InvalidateGraph()
    {
        var globalTick = DirtyFlag.GlobalInvalidationTick;
        if (globalTick == _dirtyFlag.InvalidationTick)
        {
            // do nothing
            return _dirtyFlag.SourceVersion;
        }

        // MultiInputSlot, TimeClipSlot, TransformCallbackSlot, etc
        if (HasInvalidationOverride) 
        {
            var target = InvalidationOverride();
            _dirtyFlag.SourceVersion = target;
            _dirtyFlag.InvalidationTick = globalTick;
            return target;
        }

        // connected
        if (InputConnections.Length > 0)
        {
            var target = InputConnections[0].InvalidateGraph();
            _dirtyFlag.SourceVersion = target;
            _dirtyFlag.InvalidationTick = globalTick;
            return target;
        }
 
        // unconnected input slots
        if (_isInputSlot)
        {
            if(_dirtyFlag.TriggerIsEnabled)
            {
                return _dirtyFlag.Invalidate();
            }
            _dirtyFlag.InvalidationTick = globalTick;
            return _dirtyFlag.SourceVersion;
        }

        // unconnected output slots
        var parentInputs = _parent.Inputs;
        var parentInputCount = parentInputs.Count;
                
        bool outputDirty = _dirtyFlag.IsDirty;
        for (var i = 0; i < parentInputCount; i++)
        {
            var input = parentInputs[i];
            input.InvalidateGraph();
            outputDirty |= input.IsDirty;
        }

        if (outputDirty)
        {
            return _dirtyFlag.Invalidate();
        }

        _dirtyFlag.InvalidationTick = globalTick;
        return _dirtyFlag.SourceVersion;
    }
        
    protected void SetVisited() => _dirtyFlag.InvalidationTick = DirtyFlag.GlobalInvalidationTick;
        
    protected virtual int InvalidationOverride() => 0;

    Guid ISlot.Id { get => Id; set => Id = value; }
    DirtyFlag ISlot.DirtyFlag => DirtyFlag;

    // todo - this should be an action list or event? ordered execution can be important
    public virtual Action<EvaluationContext>? UpdateAction
    {
        get => _updateAction;
        set
        {
            _updateAction = value;
            RebuildEffectiveUpdateAction();
        }
    }

    protected Action<EvaluationContext>? _keepOriginalUpdateAction;
    private protected DirtyFlagTrigger _keepDirtyFlagTrigger;
    protected Slot<T>? _targetInputForBypass;
        
    private bool _isInputSlot;
    private bool _isMultiInput;
    public bool IsMultiInput => _isMultiInput;
    private MultiInputSlot<T> _thisAsMultiInputSlot = null!;
    protected MultiInputSlot<T> ThisAsMultiInputSlot => _thisAsMultiInputSlot;
    private Instance _parent = null!;
    private bool _valueIsCommand;
    private protected bool HasInvalidationOverride;
    private bool _parentIsICompoundWithUpdate;
    private Animation.TimeClip? _timeClipForOutputRemap;
    private Action<EvaluationContext>? _updateAction;

    /// <summary>What <see cref="Update"/> actually invokes: <see cref="_updateAction"/>, wrapped with the
    /// sibling-output time remap when <see cref="_timeClipForOutputRemap"/> is set. Rebuilt on assignment,
    /// never per frame.</summary>
    private Action<EvaluationContext>? _effectiveUpdateAction;

    public override string ToString()
    {
        if (_isInputSlot)
        {
            var i =  Parent.Inputs.FirstOrDefault(i => i.Id == Id);
            return i != null 
                       ? $"{i.Parent.Symbol.Name}.{i.Input.Name}" 
                       : "ISlot";
        }

        var symbol = Parent.Symbol;
        var outputDef =  Parent.Symbol.OutputDefinitions.FirstOrDefault(o => o.Id == Id);
        return outputDef != null 
                   ? $"{symbol.Name}.{outputDef.Name} (Output)" 
                   : "IOutputSlot";
    }
}
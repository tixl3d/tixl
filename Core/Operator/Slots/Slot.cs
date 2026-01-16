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

public class Slot<T> : ISlot
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
            UpdateAction?.Invoke(context);
            _dirtyFlag.Clear();
            _dirtyFlag.SetUpdated();
        }
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
                    //Log.Debug($"Skipping connection for compound op with update method for {Parent.Symbol} {this}", compoundWithUpdate);
                    //compoundWithUpdate.RegisterOutputUpdateAction(this, ConnectedUpdate);
                    ArrayUtils.InsertAtIndexOrEnd(ref InputConnections, (Slot<T>)sourceSlot, index);

                    _dirtyFlag.Target = sourceSlot.DirtyFlag.Target;
                    _dirtyFlag.Reference = _dirtyFlag.Target - 1;
                    return;
                }
            }
            UpdateAction = ConnectedUpdate;
            _dirtyFlag.Target = sourceSlot.DirtyFlag.Target;
            _dirtyFlag.Reference = _dirtyFlag.Target - 1;
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

    public int Invalidate()
    {
        var refFrame = DirtyFlag.InvalidationRefFrame;
        if (refFrame == _dirtyFlag.InvalidatedWithRefFrame)
        {
            // do nothing
            return _dirtyFlag.Target;
        }

        // MultiInputSlot, TimeClipSlot, TransformCallbackSlot, etc
        if (HasInvalidationOverride) 
        {
            var target = InvalidationOverride();
            _dirtyFlag.Target = target;
            _dirtyFlag.InvalidatedWithRefFrame = refFrame;
            return target;
        }

        // connected
        if (InputConnections.Length > 0)
        {
            var target = InputConnections[0].Invalidate();
            _dirtyFlag.Target = target;
            _dirtyFlag.InvalidatedWithRefFrame = refFrame;
            return target;
        }
 
        // unconnected input slots
        if (_isInputSlot)
        {
            if(_dirtyFlag.TriggerIsEnabled)
            {
                return _dirtyFlag.Invalidate();
            }
            _dirtyFlag.InvalidatedWithRefFrame = refFrame;
            return _dirtyFlag.Target;
        }

        // unconnected output slots
        var parentInputs = _parent.Inputs;
        var parentInputCount = parentInputs.Count;
                
        bool outputDirty = _dirtyFlag.IsDirty;
        for (var i = 0; i < parentInputCount; i++)
        {
            var input = parentInputs[i];
            input.Invalidate();
            outputDirty |= input.IsDirty;
        }

        if (outputDirty)
        {
            return _dirtyFlag.Invalidate();
        }

        _dirtyFlag.InvalidatedWithRefFrame = refFrame;
        return _dirtyFlag.Target;
    }
        
    protected void SetVisited() => _dirtyFlag.InvalidatedWithRefFrame = DirtyFlag.InvalidationRefFrame;
        
    protected virtual int InvalidationOverride() => 0;

    Guid ISlot.Id { get => Id; set => Id = value; }
    DirtyFlag ISlot.DirtyFlag => DirtyFlag;

    // todo - this should be an action list or event? ordered execution can be important
    public virtual Action<EvaluationContext>? UpdateAction { get; set; }

    protected Action<EvaluationContext>? _keepOriginalUpdateAction;
    private DirtyFlagTrigger _keepDirtyFlagTrigger;
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
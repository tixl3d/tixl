using System;

namespace T3.Core.Operator.Slots;

public class InputSlot<T> : Slot<T>, IInputSlot
{
    public Type MappedType { get; set; }

    private InputSlot(InputValue<T> typedInputValue) : base(true)
    {
        UpdateAction = InputUpdate;
        _keepOriginalUpdateAction = UpdateAction;
        TypedInputValue = typedInputValue;
        Value = typedInputValue.Value;
    }

    public InputSlot(T value) : this(new InputValue<T>(value))
    {
    }

    public InputSlot() : this(default(T))
    {
        UpdateAction = InputUpdate;
        _keepOriginalUpdateAction = UpdateAction;
    }

    public void InputUpdate(EvaluationContext context)
    {
        Value = Input.IsDefault ? TypedDefaultValue.Value : TypedInputValue.Value;
    }

    private Symbol.Child.Input _input;

    public Symbol.Child.Input Input
    {
        get => _input;
        set
        {
            _input = value;
            TypedInputValue = (InputValue<T>)value.Value;
            TypedDefaultValue = (InputValue<T>)value.DefaultValue;

            if (_input.IsDefault && TypedDefaultValue.IsEditableInputReferenceType)
            {
                TypedInputValue.AssignClone(TypedDefaultValue);
            }
        }
    }

    public T GetCurrentValue()
    {
        return HasInputConnections || (Parent.Parent?.Symbol.Animator.IsInputSlotAnimated(this) ?? false) 
                   ? Value
                   : TypedInputValue.Value;
    }

    public void SetTypedInputValue(T newValue)
    {
        Input.IsDefault = false;
        TypedInputValue.Value = newValue;
        Value = newValue;
        DirtyFlag.Invalidate();
    }
        
    public bool TryGetAsMultiInput(out IMultiInputSlot multiInput)
    {
        multiInput = ThisAsMultiInputSlot;
        return IsMultiInput;
    }

    public InputValue<T> TypedInputValue;
    public InputValue<T> TypedDefaultValue;
        
    bool IInputSlot.IsMultiInput => IsMultiInput;
    public bool IsDirty => DirtyFlag.IsDirty;
    void IInputSlot.SetVisited() => SetVisited();
}